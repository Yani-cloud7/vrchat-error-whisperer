using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRChatUtility.ErrorWhisperer.Editor
{
    public sealed class ErrorWhispererWindow : EditorWindow
    {
        private Vector2 scroll;
        private string consoleText = string.Empty;
        private AnalysisResult result = AnalysisResult.Empty;
        private bool questTarget = true;
        private bool showConsoleText;
        private readonly Dictionary<string, bool> expandedFindings = new Dictionary<string, bool>();
        private List<SuspectAsset> suspectAssets = new List<SuspectAsset>();
        private string suspectScanSummary = "Not scanned yet.";
        private static readonly Dictionary<string, Texture2D> ColorTextures = new Dictionary<string, Texture2D>();

        [MenuItem("Tools/VRChat Utility/Error Whisperer")]
        public static void Open()
        {
            var window = GetWindow<ErrorWhispererWindow>();
            window.titleContent = new GUIContent("Error Whisperer");
            window.minSize = new Vector2(620, 520);
            window.RefreshFromConsole();
        }

        private void OnGUI()
        {
            DrawToolbar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawScore();
            DrawConsoleSummary();
            DrawSuspectScan();
            DrawFindings();
            DrawRawInput();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Analyze Current Console", EditorStyles.toolbarButton, GUILayout.Width(170)))
                {
                    RefreshFromConsole();
                }

                if (GUILayout.Button("Analyze Text Below", EditorStyles.toolbarButton, GUILayout.Width(135)))
                {
                    Analyze();
                }

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(56)))
                {
                    consoleText = string.Empty;
                    result = AnalysisResult.Empty;
                    suspectAssets.Clear();
                    suspectScanSummary = "Not scanned yet.";
                }

                GUILayout.FlexibleSpace();
                questTarget = GUILayout.Toggle(questTarget, "Quest target", EditorStyles.toolbarButton, GUILayout.Width(100));

                if (GUILayout.Button("Find Udon Assets", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    ScanSuspectAssets();
                }

                EditorGUI.BeginDisabledGroup(suspectAssets.Count == 0);
                if (GUILayout.Button("Copy Suspects", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildSuspectReport();
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("Copy Report", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildReport();
                }

                EditorGUI.BeginDisabledGroup(!result.Findings.Any(finding => finding.CaseType == "upload-support"));
                if (GUILayout.Button("Copy Support Summary", EditorStyles.toolbarButton, GUILayout.Width(145)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildSupportSummary();
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawScore()
        {
            using (new EditorGUILayout.HorizontalScope(PanelStyle(new Color(0.22f, 0.22f, 0.22f))))
            {
                DrawMetric("Error entries", result.ErrorCount.ToString(), result.ErrorCount > 0 ? ErrorColor() : GoodColor());
                DrawMetric("Warnings", result.WarningCount.ToString(), result.WarningCount > 0 ? WarningColor() : GoodColor());
                DrawMetric("Readiness", result.RawLineCount > 0 ? result.Score + "/100" : "-", ScoreColor(result.Score));
                DrawMetric("Upload chance", result.RawLineCount > 0 ? result.UploadChance : "-", ChanceColor(result.UploadChance));
            }
        }

        private void DrawConsoleSummary()
        {
            var color = result.ErrorCount > 0 ? new Color(0.30f, 0.22f, 0.20f) : new Color(0.20f, 0.27f, 0.22f);
            using (new EditorGUILayout.VerticalScope(PanelStyle(color)))
            {
                var primary = result.Findings.FirstOrDefault();
                var summary = primary != null
                    ? primary.Title + " -> " + primary.Fix
                    : result.ErrorCount == 0 && result.RawLineCount > 0
                        ? "0 errors found. Nothing needs fixing first."
                        : result.RawLineCount == 0
                            ? "Analyze the current console or paste a failed build/upload log."
                            : "No corpus match for the current errors.";
                EditorGUILayout.LabelField(summary, WrappedStyle());
                EditorGUILayout.LabelField(result.RootCauseCount + " root cause" + (result.RootCauseCount == 1 ? "" : "s") + ", " + result.RawLineCount + " lines scanned, " + result.DuplicateCount + " duplicate/noise lines collapsed.", SmallMutedStyle());
            }
        }

        private void DrawFindings()
        {
            DrawLane(PriorityLane.UploadSupport, "Upload/support escalation", "VRChat-side, account, blueprint, or upload API issues.");
            DrawLane(PriorityLane.FixFirst, "Fix first");
            DrawLane(PriorityLane.Then, "Then");
            DrawLane(PriorityLane.Warning, "Warnings", "Real warnings, but not build blockers.");
            DrawLane(PriorityLane.Related, "Known related issues", "Historical or workflow matches. Use as context, not proof.");
            DrawLane(PriorityLane.Later, "Ignore until later");

            if (!result.Findings.Any())
            {
                var message = result.RawLineCount == 0
                    ? "Click Analyze Current Console after a failed build/upload."
                    : result.ErrorCount == 0
                        ? "0 errors found. Nothing needs fixing first."
                        : "No corpus match for the current errors.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private void DrawSuspectScan()
        {
            if (result.ErrorCount == 0 && suspectAssets.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Suspect asset scan", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Find Udon Assets", GUILayout.Width(125)))
                    {
                        ScanSuspectAssets();
                    }
                }

                EditorGUILayout.LabelField(suspectScanSummary, SmallMutedStyle());

                if (suspectAssets.Count == 0)
                {
                    EditorGUILayout.LabelField("Looks for broken or missing Udon/UdonSharp references in scene, prefab, and asset files.", SmallMutedStyle());
                    return;
                }

                var first = suspectAssets[0];
                EditorGUILayout.LabelField("Inspect first: " + first.Path, EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(first.NextAction))
                {
                    EditorGUILayout.LabelField(first.NextAction, SmallMutedStyle());
                }

                foreach (var suspect in suspectAssets.Take(12))
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(suspect.Path, WrappedStyle());
                            if (GUILayout.Button("Select", GUILayout.Width(58)))
                            {
                                SelectAsset(suspect.Path);
                            }

                            if (GUILayout.Button("Ping", GUILayout.Width(48)))
                            {
                                PingAsset(suspect.Path);
                            }

                            if (GUILayout.Button("Copy", GUILayout.Width(48)))
                            {
                                EditorGUIUtility.systemCopyBuffer = suspect.Path;
                            }
                        }

                        EditorGUILayout.LabelField(suspect.Classification, EditorStyles.miniBoldLabel);
                        if (suspect.IsInCurrentScene)
                        {
                            EditorGUILayout.LabelField("Likely active in current open scene", EditorStyles.miniBoldLabel);
                        }

                        EditorGUILayout.LabelField(suspect.Reason, SmallMutedStyle());
                        if (!string.IsNullOrEmpty(suspect.Context))
                        {
                            EditorGUILayout.LabelField("Context: " + suspect.Context, SmallMutedStyle());
                        }

                        EditorGUILayout.LabelField(suspect.ReferenceSummary, SmallMutedStyle());
                        if (!string.IsNullOrEmpty(suspect.NextAction))
                        {
                            EditorGUILayout.LabelField("Next: " + suspect.NextAction, WrappedStyle());
                        }

                        foreach (var reference in suspect.References.Take(3))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("Used by: " + reference, SmallMutedStyle());
                                if (GUILayout.Button("Select Owner", GUILayout.Width(92)))
                                {
                                    SelectAsset(reference);
                                }

                                if (GUILayout.Button("Ping", GUILayout.Width(48)))
                                {
                                    PingAsset(reference);
                                }
                            }
                        }
                    }
                }

                if (suspectAssets.Count > 12)
                {
                    EditorGUILayout.LabelField("Showing 12 of " + suspectAssets.Count + ". Use Copy Suspects for the full list.", SmallMutedStyle());
                }
            }
        }

        private void DrawLane(PriorityLane lane, string label, string description = null)
        {
            var findings = result.Findings.Where(finding => finding.Lane == lane).ToList();
            if (findings.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(description))
            {
                EditorGUILayout.LabelField(description, SmallMutedStyle());
            }

            foreach (var finding in findings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var expanded = IsExpanded(finding);
                    var stripe = GUILayoutUtility.GetRect(3, 1, GUILayout.ExpandHeight(false));
                    stripe.height = 3;
                    EditorGUI.DrawRect(stripe, LaneColor(finding.Lane));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expanded = EditorGUILayout.Foldout(expanded, finding.Title, true);
                        expandedFindings[finding.Id] = expanded;
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(finding.Confidence + "%", BadgeStyle(LaneColor(finding.Lane)), GUILayout.Width(42));
                    }

                    EditorGUILayout.LabelField("Next: " + finding.Fix, WrappedStyle());

                    if (expanded)
                    {
                        EditorGUILayout.LabelField("Why: " + finding.Cause, WrappedStyle());

                        if (finding.Evidence.Count > 0)
                        {
                            EditorGUILayout.LabelField("Evidence", EditorStyles.miniBoldLabel);
                            foreach (var evidence in finding.Evidence)
                            {
                                var suffix = evidence.Count > 1 ? " (" + evidence.Count + "x)" : string.Empty;
                                EditorGUILayout.LabelField(evidence.Line + suffix, SmallMutedStyle());
                                foreach (var suspect in evidence.Suspects)
                                {
                                    EditorGUILayout.LabelField("Suspect: " + suspect, SmallMutedStyle());
                                }
                            }
                        }

                        if (finding.Recommendations.Length > 0)
                        {
                            EditorGUILayout.LabelField("Notes", EditorStyles.miniBoldLabel);
                            foreach (var recommendation in finding.Recommendations)
                            {
                                EditorGUILayout.LabelField("- " + recommendation, SmallMutedStyle());
                            }
                        }

                        EditorGUILayout.LabelField("Match: " + finding.CaseType + " / " + finding.MatchStrictness, SmallMutedStyle());
                    }
                }
            }
        }

        private bool IsExpanded(Finding finding)
        {
            bool expanded;
            if (expandedFindings.TryGetValue(finding.Id, out expanded))
            {
                return expanded;
            }

            return finding.Lane == PriorityLane.UploadSupport || finding.Lane == PriorityLane.FixFirst;
        }

        private void DrawRawInput()
        {
            EditorGUILayout.Space(10);
            showConsoleText = EditorGUILayout.Foldout(showConsoleText, "Console Text", true);
            if (showConsoleText)
            {
                consoleText = EditorGUILayout.TextArea(consoleText, GUILayout.MinHeight(140));
            }
        }

        private void RefreshFromConsole()
        {
            consoleText = UnityConsoleReader.ReadAll();
            Analyze();
        }

        private void Analyze()
        {
            result = Analyzer.Analyze(consoleText, questTarget);
            Repaint();
        }

        private void ScanSuspectAssets()
        {
            suspectAssets = SuspectAssetScanner.Scan();
            suspectScanSummary = suspectAssets.Count == 0
                ? "No obvious broken Udon/UdonSharp serialized references found under Assets."
                : suspectAssets.Count + " possible Udon/UdonSharp asset issue" + (suspectAssets.Count == 1 ? "" : "s") + " found. Review these paths before deleting or replacing anything.";
            Repaint();
        }

        private static void SelectAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorUtility.FocusProjectWindow();
        }

        private static void PingAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return;
            }

            EditorGUIUtility.PingObject(asset);
        }

        private string BuildReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("VRChat Error Whisperer");
            builder.AppendLine("Build readiness: " + (result.HasFindings ? result.Score + "/100" : "-"));
            builder.AppendLine("Estimated upload chance: " + (result.HasFindings ? result.UploadChance : "-"));
            builder.AppendLine("Root causes detected: " + result.RootCauseCount);
            builder.AppendLine();

            foreach (var lane in new[] { PriorityLane.UploadSupport, PriorityLane.FixFirst, PriorityLane.Then, PriorityLane.Warning, PriorityLane.Related, PriorityLane.Later })
            {
                var findings = result.Findings.Where(finding => finding.Lane == lane).ToList();
                if (findings.Count == 0)
                {
                    continue;
                }

                builder.AppendLine(LaneLabel(lane) + ":");
                foreach (var finding in findings)
                {
                    builder.AppendLine("- " + finding.Title + ": " + finding.Fix);
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private string BuildSupportSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine("VRChat upload support summary");
            builder.AppendLine("Errors: " + result.ErrorCount);
            builder.AppendLine("Warnings: " + result.WarningCount);
            builder.AppendLine();

            foreach (var finding in result.Findings.Where(finding => finding.CaseType == "upload-support"))
            {
                builder.AppendLine("Matched issue: " + finding.Title);
                builder.AppendLine("Why it looks VRChat-side: " + finding.Cause);
                builder.AppendLine("Requested next step: " + finding.Fix);

                if (finding.Evidence.Count > 0)
                {
                    builder.AppendLine("Evidence:");
                    foreach (var evidence in finding.Evidence)
                    {
                        builder.AppendLine("- " + evidence.Line);
                        foreach (var suspect in evidence.Suspects)
                        {
                            builder.AppendLine("  Suspect: " + suspect);
                        }
                    }
                }

                builder.AppendLine();
            }

            builder.AppendLine("Attach the full SDK log, blueprint ID, world ID, account name, and approximate upload time.");
            return builder.ToString();
        }

        private string BuildSuspectReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("VRChat Error Whisperer suspect asset scan");
            builder.AppendLine(suspectScanSummary);
            builder.AppendLine();

            foreach (var suspect in suspectAssets)
            {
                builder.AppendLine("- " + suspect.Path);
                builder.AppendLine("  Type: " + suspect.Classification);
                if (suspect.IsInCurrentScene)
                {
                    builder.AppendLine("  Current scene: likely active in current open scene");
                }

                builder.AppendLine("  Reason: " + suspect.Reason);
                if (!string.IsNullOrEmpty(suspect.Context))
                {
                    builder.AppendLine("  Context: " + suspect.Context);
                }

                builder.AppendLine("  " + suspect.ReferenceSummary);
                if (!string.IsNullOrEmpty(suspect.NextAction))
                {
                    builder.AppendLine("  Next: " + suspect.NextAction);
                }

                foreach (var reference in suspect.References)
                {
                    builder.AppendLine("  Used by: " + reference);
                }
            }

            return builder.ToString();
        }

        private static string LaneLabel(PriorityLane lane)
        {
            switch (lane)
            {
                case PriorityLane.UploadSupport:
                    return "Upload/support escalation";
                case PriorityLane.FixFirst:
                    return "Fix first";
                case PriorityLane.Then:
                    return "Then";
                case PriorityLane.Warning:
                    return "Warnings";
                case PriorityLane.Related:
                    return "Known related issues";
                default:
                    return "Ignore until later";
            }
        }

        private static GUIStyle WrappedStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                wordWrap = true
            };
        }

        private static GUIStyle SmallMutedStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };
        }

        private static void DrawMetric(string label, string value, Color color)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(105)))
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(value, MetricStyle(color));
            }
        }

        private static GUIStyle MetricStyle(Color color)
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = color }
            };
        }

        private static GUIStyle BadgeStyle(Color color)
        {
            return new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = color }
            };
        }

        private static GUIStyle PanelStyle(Color color)
        {
            var style = new GUIStyle(EditorStyles.helpBox);
            style.normal.background = TextureForColor(color);
            return style;
        }

        private static Texture2D TextureForColor(Color color)
        {
            var key = ColorUtility.ToHtmlStringRGBA(color);
            Texture2D cached;
            if (ColorTextures.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(1, 1);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            ColorTextures[key] = texture;
            return texture;
        }

        private static Color LaneColor(PriorityLane lane)
        {
            switch (lane)
            {
                case PriorityLane.UploadSupport:
                    return new Color(0.68f, 0.45f, 0.95f);
                case PriorityLane.FixFirst:
                    return ErrorColor();
                case PriorityLane.Then:
                    return new Color(0.95f, 0.62f, 0.25f);
                case PriorityLane.Warning:
                    return WarningColor();
                case PriorityLane.Related:
                    return new Color(0.45f, 0.68f, 0.95f);
                default:
                    return new Color(0.55f, 0.55f, 0.55f);
            }
        }

        private static Color ScoreColor(int score)
        {
            return score >= 82 ? GoodColor() : score >= 58 ? WarningColor() : ErrorColor();
        }

        private static Color ChanceColor(string chance)
        {
            return chance == "High" || chance == "Clean" || chance == "No blocking errors" ? GoodColor() :
                chance == "Medium" ? WarningColor() : ErrorColor();
        }

        private static Color ErrorColor()
        {
            return new Color(1.0f, 0.42f, 0.36f);
        }

        private static Color WarningColor()
        {
            return new Color(1.0f, 0.78f, 0.30f);
        }

        private static Color GoodColor()
        {
            return new Color(0.44f, 0.86f, 0.52f);
        }

        private static class Analyzer
        {
            private static readonly Regex CompilerErrorPattern = new Regex(
                @"(?<path>(?:Assets|Packages)[\\/][^:(\r\n]+\.cs)\((?<line>\d+),(?<column>\d+)\):\s*error\s*(?<code>CS\d{4}):\s*(?<message>[^\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            private static readonly Regex[] NoisePatterns =
            {
                new Regex("begin mono manager reload", RegexOptions.IgnoreCase),
                new Regex("refreshing native plugins", RegexOptions.IgnoreCase),
                new Regex("assetdatabase", RegexOptions.IgnoreCase),
                new Regex("domain reload", RegexOptions.IgnoreCase),
                new Regex("repaint", RegexOptions.IgnoreCase),
                new Regex("layout rebuild", RegexOptions.IgnoreCase)
            };

            public static AnalysisResult Analyze(string text, bool includeQuestRules)
            {
                var lines = SplitLines(text);
                var entries = SplitEntries(lines)
                    .Where(entry => !NoisePatterns.Any(pattern => pattern.IsMatch(entry.FirstLine)))
                    .ToList();
                var errorEntries = entries.Where(entry => entry.IsError).ToList();
                var warningCount = entries.Count(entry => entry.IsWarning);
                var errorCount = errorEntries.Count;

                if (lines.Count > 0 && errorCount == 0)
                {
                    return new AnalysisResult(
                        new List<Finding>(),
                        lines.Count,
                        0,
                        0,
                        warningCount,
                        entries.Count - entries.Select(entry => NormalizeLine(entry.FirstLine)).Distinct().Count(),
                        100,
                        warningCount > 0 ? "No blocking errors" : "Clean");
                }

                var groups = entries
                    .GroupBy(entry => NormalizeLine(entry.FirstLine))
                    .Select(group => new EvidenceLine(group.First().FirstLine, group.Count(), ExtractSuspects(group.Select(entry => entry.Text))))
                    .ToList();

                var errorGroups = errorEntries
                    .GroupBy(entry => NormalizeLine(entry.FirstLine))
                    .Select(group => new EvidenceLine(group.First().FirstLine, group.Count(), ExtractSuspects(group.Select(entry => entry.Text))))
                    .ToList();

                var textLower = string.Join("\n", errorEntries.Select(entry => entry.Text)).ToLowerInvariant();
                var findings = CorpusLoader.Cases
                    .Select(corpusCase => MatchCase(corpusCase, textLower, errorGroups, includeQuestRules))
                    .Where(finding => finding != null)
                    .Concat(BuildCompilerFindings(errorEntries))
                    .OrderByDescending(finding => finding.Priority)
                    .Take(8)
                    .ToList();

                var score = Math.Max(0, 100 - findings.Sum(finding => finding.ScorePenalty));
                var chance = score >= 82 ? "High" : score >= 58 ? "Medium" : "Low";

                return new AnalysisResult(
                    findings,
                    lines.Count,
                    errorCount,
                    findings.Count,
                    warningCount,
                    entries.Count - groups.Count,
                    score,
                    chance);
            }

            private static IEnumerable<Finding> BuildCompilerFindings(List<ConsoleEntry> errorEntries)
            {
                var compilerMatches = errorEntries
                    .Select(entry => new { Entry = entry, Match = CompilerErrorPattern.Match(entry.Text) })
                    .Where(item => item.Match.Success)
                    .GroupBy(item => item.Match.Groups["path"].Value.Replace('\\', '/') + "|" + item.Match.Groups["code"].Value)
                    .Take(5);

                foreach (var group in compilerMatches)
                {
                    var first = group.First();
                    var match = first.Match;
                    var path = match.Groups["path"].Value.Replace('\\', '/');
                    var code = match.Groups["code"].Value;
                    var line = match.Groups["line"].Value;
                    var column = match.Groups["column"].Value;
                    var message = match.Groups["message"].Value.Trim();
                    var title = "C# compile error in " + Path.GetFileName(path) + " (" + code + ")";
                    var fix = "Open " + path + " at line " + line + ", column " + column + " and fix this compiler error first.";
                    var rootCause = "Unity cannot compile this script: " + message + ". VRChat/Udon analysis is secondary until C# compiles.";
                    var recommendations = CompilerRecommendations(code);
                    var evidence = group
                        .Select(item => new EvidenceLine(item.Entry.FirstLine, 1, new[] { path + ":" + line }))
                        .ToList();

                    var syntheticCase = new CorpusCase
                    {
                        id = "generic-csharp-" + code.ToLowerInvariant() + "-" + NormalizeId(path),
                        title = title,
                        category = "udon-compile",
                        severity = "blocker",
                        caseType = "console-error",
                        matchStrictness = "exact",
                        rootCause = rootCause,
                        fixFirst = fix,
                        recommendations = recommendations,
                        priorityBoost = 20
                    };

                    yield return new Finding(syntheticCase, evidence, 70);
                }
            }

            private static string[] CompilerRecommendations(string code)
            {
                switch (code)
                {
                    case "CS1002":
                        return new[] { "CS1002 usually means a missing semicolon before or at the reported location.", "Fix the first compiler error, then re-run UdonSharp compile." };
                    case "CS1022":
                        return new[] { "CS1022 often means an extra brace or code outside the class/namespace.", "Check the braces above the reported line before changing VRChat settings." };
                    case "CS1513":
                        return new[] { "CS1513 usually means a missing closing brace.", "Use the first reported line as the starting point; later compiler errors may be secondary." };
                    case "CS0103":
                        return new[] { "CS0103 means the name is not in scope. Check spelling, fields, methods, and using statements.", "If this is UdonSharp, confirm the API exists in your installed SDK version." };
                    case "CS0246":
                        return new[] { "CS0246 means a type or namespace is missing. Check package dependencies and using statements.", "If the type comes from a package, confirm the package imported correctly." };
                    default:
                        return new[] { "Fix compiler errors before debugging SDK upload, Udon sync, or Quest issues.", "Start with the first error in this script; later errors may be repeats." };
                }
            }

            private static string NormalizeId(string value)
            {
                var builder = new StringBuilder();
                foreach (var character in value.ToLowerInvariant())
                {
                    builder.Append(char.IsLetterOrDigit(character) ? character : '-');
                }

                return builder.ToString().Trim('-');
            }

            private static List<ConsoleEntry> SplitEntries(List<string> lines)
            {
                var entries = new List<ConsoleEntry>();
                var builder = new StringBuilder();
                string firstLine = null;
                var currentKind = ConsoleEntryKind.Log;

                foreach (var line in lines)
                {
                    if (IsEntryStart(line))
                    {
                        AddEntry(entries, builder, firstLine, currentKind);
                        builder.Length = 0;
                        firstLine = line;
                        currentKind = IsErrorLine(line) ? ConsoleEntryKind.Error : IsWarningLine(line) ? ConsoleEntryKind.Warning : ConsoleEntryKind.Log;
                    }
                    else if (firstLine == null)
                    {
                        firstLine = line;
                        currentKind = IsErrorLine(line) ? ConsoleEntryKind.Error : IsWarningLine(line) ? ConsoleEntryKind.Warning : ConsoleEntryKind.Log;
                    }

                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(line);
                }

                AddEntry(entries, builder, firstLine, currentKind);
                return entries;
            }

            private static void AddEntry(List<ConsoleEntry> entries, StringBuilder builder, string firstLine, ConsoleEntryKind kind)
            {
                if (firstLine == null || builder.Length == 0)
                {
                    return;
                }

                entries.Add(new ConsoleEntry(firstLine, builder.ToString(), kind));
            }

            private static bool IsEntryStart(string line)
            {
                return Regex.IsMatch(line ?? string.Empty, @"^\[(Error|Warning|Log|Assert|Fatal)\]", RegexOptions.IgnoreCase);
            }

            private static string[] ExtractSuspects(IEnumerable<string> texts)
            {
                var suspects = new List<string>();
                foreach (var text in texts)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    foreach (Match match in Regex.Matches(text, @"(?im)^Context: (.+)$"))
                    {
                        AddUnique(suspects, match.Groups[1].Value.Trim());
                    }

                    foreach (Match match in Regex.Matches(text, @"Assets[\\/][^\r\n:]+?\.(asset|prefab|unity|cs|shader|cginc)", RegexOptions.IgnoreCase))
                    {
                        AddUnique(suspects, match.Value.Trim());
                    }
                }

                return suspects.Take(4).ToArray();
            }

            private static void AddUnique(List<string> values, string value)
            {
                if (string.IsNullOrEmpty(value) || values.Contains(value))
                {
                    return;
                }

                values.Add(value);
            }

            private static bool IsErrorLine(string line)
            {
                return Regex.IsMatch(line ?? string.Empty, @"^\[Error\]|^\[Assert\]|^\[Fatal\]|\berror\s+CS\d{4}\b|\bException\b|UnityException|AssetBundle was not built|Building AssetBundle failed|Upload failed|Build failed|\bfails?\b|\bfailure\b", RegexOptions.IgnoreCase);
            }

            private static bool IsWarningLine(string line)
            {
                return Regex.IsMatch(line ?? string.Empty, @"^\[Warning\]|\bwarning\s+CS\d{4}\b|obsolete|deprecated", RegexOptions.IgnoreCase);
            }

            private static Finding MatchCase(CorpusCase corpusCase, string textLower, List<EvidenceLine> groups, bool includeQuestRules)
            {
                if (corpusCase == null || corpusCase.rawSignals == null || corpusCase.rawSignals.Length == 0)
                {
                    return null;
                }

                if (!includeQuestRules && IsQuestCase(corpusCase))
                {
                    return null;
                }

                var matchedSignalCount = 0;
                var bestScore = 0;
                var hasExactSignal = false;

                foreach (var signal in corpusCase.rawSignals)
                {
                    var score = ScoreSignal(signal, textLower);
                    if (score <= 0)
                    {
                        continue;
                    }

                    matchedSignalCount++;
                    bestScore = Math.Max(bestScore, score);
                    hasExactSignal = hasExactSignal || score >= 100;
                }

                if (corpusCase.regexSignals != null)
                {
                    foreach (var signal in corpusCase.regexSignals)
                    {
                        var score = ScoreRegexSignal(signal, textLower);
                        if (score <= 0)
                        {
                            continue;
                        }

                        matchedSignalCount++;
                        bestScore = Math.Max(bestScore, score);
                        hasExactSignal = true;
                    }
                }

                if (matchedSignalCount == 0)
                {
                    return null;
                }

                var totalScore = Math.Min(100, bestScore + Math.Max(0, matchedSignalCount - 1) * 12);
                if (!PassesStrictness(corpusCase, totalScore, hasExactSignal))
                {
                    return null;
                }

                var evidence = groups
                    .Select(group => new
                    {
                        Line = group,
                        Score = Math.Max(
                            corpusCase.rawSignals.Max(signal => ScoreLineAgainstSignal(group.Line, signal)),
                            corpusCase.regexSignals != null && corpusCase.regexSignals.Any(signal => RegexSignalMatches(signal, group.Line)) ? 100 : 0)
                    })
                    .Where(item => item.Score > 0)
                    .OrderByDescending(item => item.Score)
                    .Take(2)
                    .Select(item => item.Line)
                    .ToList();

                return new Finding(corpusCase, evidence, totalScore);
            }

            private static int ScoreRegexSignal(string signal, string textLower)
            {
                return RegexSignalMatches(signal, textLower) ? 100 : 0;
            }

            private static bool RegexSignalMatches(string signal, string value)
            {
                if (string.IsNullOrEmpty(signal) || string.IsNullOrEmpty(value))
                {
                    return false;
                }

                try
                {
                    return Regex.IsMatch(value, signal, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            private static bool PassesStrictness(CorpusCase corpusCase, int totalScore, bool hasExactSignal)
            {
                var strictness = string.IsNullOrEmpty(corpusCase.matchStrictness) ? "strong" : corpusCase.matchStrictness;
                if (strictness == "exact")
                {
                    return hasExactSignal;
                }

                if (strictness == "related")
                {
                    return totalScore >= 80;
                }

                return totalScore >= 65;
            }

            private static bool IsQuestCase(CorpusCase corpusCase)
            {
                var builder = new StringBuilder();
                builder.Append(corpusCase.title);
                if (corpusCase.rawSignals != null)
                {
                    builder.Append(" ");
                    builder.Append(string.Join(" ", corpusCase.rawSignals));
                }

                return Regex.IsMatch(builder.ToString(), @"\bquest\b|android", RegexOptions.IgnoreCase);
            }

            private static int ScoreSignal(string signal, string textLower)
            {
                var signalLower = (signal ?? string.Empty).ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(signalLower))
                {
                    return 0;
                }

                if (textLower.Contains(signalLower))
                {
                    return 100;
                }

                var tokens = Tokenize(signalLower);
                if (tokens.Count == 0)
                {
                    return 0;
                }

                var hits = tokens.Count(token => textLower.Contains(token));
                var ratio = hits / (float)tokens.Count;
                if (tokens.Count <= 2)
                {
                    return 0;
                }

                if (ratio >= 0.85f && hits >= 3)
                {
                    return 55 + Mathf.RoundToInt(ratio * 25f);
                }

                if (ratio >= 0.65f && hits >= 4)
                {
                    return 30 + Mathf.RoundToInt(ratio * 20f);
                }

                return 0;
            }

            private static int ScoreLineAgainstSignal(string line, string signal)
            {
                var lineLower = (line ?? string.Empty).ToLowerInvariant();
                var signalLower = (signal ?? string.Empty).ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(signalLower))
                {
                    return 0;
                }

                if (lineLower.Contains(signalLower) || signalLower.Contains(lineLower))
                {
                    return 100;
                }

                var tokens = Tokenize(signalLower);
                if (tokens.Count == 0)
                {
                    return 0;
                }

                var hits = tokens.Count(token => lineLower.Contains(token));
                var ratio = hits / (float)tokens.Count;
                return ratio >= 0.65f && hits >= 3 ? Mathf.RoundToInt(ratio * 80f) : 0;
            }

            private static List<string> Tokenize(string value)
            {
                return Regex.Matches(value ?? string.Empty, @"[a-z0-9_./#-]{3,}")
                    .Cast<Match>()
                    .Select(match => match.Value)
                    .Where(token => !StopWords.Contains(token))
                    .Distinct()
                    .ToList();
            }

            private static readonly HashSet<string> StopWords = new HashSet<string>
            {
                "the", "and", "that", "this", "with", "from", "into", "your", "you", "are", "was", "were",
                "but", "not", "can", "cannot", "after", "before", "then", "than", "only", "even", "still",
                "when", "where", "while", "because", "unity", "vrchat", "build", "error", "failed", "fails"
            };

            private static List<string> SplitLines(string text)
            {
                return (text ?? string.Empty)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();
            }

            private static string NormalizeLine(string line)
            {
                var normalized = Regex.Replace(line, @"\(\d+,\d+\)", "(line,column)");
                normalized = Regex.Replace(normalized, @"\bline \d+\b", "line n", RegexOptions.IgnoreCase);
                return Regex.Replace(normalized, @"\s+", " ").Trim();
            }
        }

        private static class UnityConsoleReader
        {
            public static string ReadAll()
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                var logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                if (logEntriesType == null || logEntryType == null)
                {
                    return "Could not read Unity Console through reflection. Paste console text here and click Analyze Text Below.";
                }

                var startGettingEntries = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var endGettingEntries = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var getEntryInternal = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (startGettingEntries == null || endGettingEntries == null || getCount == null || getEntryInternal == null)
                {
                    return "Could not read Unity Console in this Unity version. Paste console text here and click Analyze Text Below.";
                }

                var entry = Activator.CreateInstance(logEntryType);
                var messageField = logEntryType.GetField("message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var fileField = logEntryType.GetField("file", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var lineField = logEntryType.GetField("line", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var instanceIdField = logEntryType.GetField("instanceID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var builder = new StringBuilder();

                try
                {
                    startGettingEntries.Invoke(null, null);
                    var count = (int)getCount.Invoke(null, null);
                    for (var i = 0; i < count; i++)
                    {
                        getEntryInternal.Invoke(null, new[] { i, entry });
                        var message = messageField != null ? messageField.GetValue(entry) as string : string.Empty;
                        var file = fileField != null ? fileField.GetValue(entry) as string : string.Empty;
                        var line = lineField != null ? (int)lineField.GetValue(entry) : 0;
                        var mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                        var instanceId = instanceIdField != null ? (int)instanceIdField.GetValue(entry) : 0;
                        var prefix = PrefixForMode(mode);

                        if (!string.IsNullOrEmpty(message))
                        {
                            builder.AppendLine(prefix + message);
                        }

                        if (!string.IsNullOrEmpty(file))
                        {
                            builder.AppendLine(file + (line > 0 ? ":" + line : string.Empty));
                        }

                        var context = ContextForInstanceId(instanceId);
                        if (!string.IsNullOrEmpty(context))
                        {
                            builder.AppendLine("Context: " + context);
                        }
                    }
                }
                finally
                {
                    endGettingEntries.Invoke(null, null);
                }

                return builder.ToString();
            }

            private static string ContextForInstanceId(int instanceId)
            {
                if (instanceId == 0)
                {
                    return string.Empty;
                }

                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj == null)
                {
                    return string.Empty;
                }

                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    return assetPath + " (" + obj.name + ")";
                }

                var gameObject = obj as GameObject;
                if (gameObject != null)
                {
                    return ScenePath(gameObject);
                }

                var component = obj as Component;
                if (component != null)
                {
                    return ScenePath(component.gameObject) + " / " + component.GetType().Name;
                }

                return obj.name + " (" + obj.GetType().Name + ")";
            }

            private static string ScenePath(GameObject gameObject)
            {
                var names = new List<string>();
                var current = gameObject.transform;
                while (current != null)
                {
                    names.Add(current.name);
                    current = current.parent;
                }

                names.Reverse();
                return string.Join("/", names.ToArray());
            }

            private static string PrefixForMode(int mode)
            {
                if ((mode & (1 | 2 | 16 | 64 | 256)) != 0)
                {
                    return "[Error] ";
                }

                if ((mode & (128 | 512)) != 0)
                {
                    return "[Warning] ";
                }

                return "[Log] ";
            }
        }

        private static class CorpusLoader
        {
            private const string PackageCorpusPath = "Packages/com.yanicloud7.error-whisperer/Editor/vrchat-error-corpus.json";
            private const string WorkspaceCorpusPath = "validation/errors/vrchat-error-corpus.json";
            private static CorpusCase[] cachedCases;

            public static CorpusCase[] Cases
            {
                get
                {
                    if (cachedCases == null)
                    {
                        cachedCases = LoadCases();
                    }

                    return cachedCases;
                }
            }

            private static CorpusCase[] LoadCases()
            {
                var path = FindCorpusPath();
                if (!File.Exists(path))
                {
                    Debug.LogWarning("Error Whisperer corpus not found. Expected " + PackageCorpusPath);
                    return new CorpusCase[0];
                }

                try
                {
                    var corpus = JsonUtility.FromJson<CorpusDocument>(File.ReadAllText(path));
                    return corpus != null && corpus.cases != null ? corpus.cases : new CorpusCase[0];
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Error Whisperer could not parse corpus: " + exception.Message);
                    return new CorpusCase[0];
                }
            }

            private static string FindCorpusPath()
            {
                foreach (var guid in AssetDatabase.FindAssets("vrchat-error-corpus"))
                {
                    var candidate = AssetDatabase.GUIDToAssetPath(guid);
                    if (candidate.EndsWith("vrchat-error-corpus.json", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                if (File.Exists(PackageCorpusPath))
                {
                    return PackageCorpusPath;
                }

                return WorkspaceCorpusPath;
            }
        }

        [Serializable]
        private sealed class CorpusDocument
        {
            public string name;
            public string version;
            public string sourceProject;
            public CorpusCase[] cases;
        }

        [Serializable]
        private sealed class CorpusCase
        {
            public string id;
            public string title;
            public string category;
            public string severity;
            public string[] rawSignals;
            public string rootCause;
            public string fixFirst;
            public string[] ignoreUntilLater;
            public string[] vrchatSpecificNotes;
            public string[] recommendations;
            public string caseType;
            public string matchStrictness;
            public bool shouldShowWhenNoErrors;
            public string[] regexSignals;
            public int priorityBoost;
        }

        private sealed class Finding
        {
            public Finding(CorpusCase corpusCase, List<EvidenceLine> evidence, int matchScore)
            {
                Id = string.IsNullOrEmpty(corpusCase.id) ? corpusCase.title : corpusCase.id;
                Title = string.IsNullOrEmpty(corpusCase.title) ? corpusCase.id : corpusCase.title;
                Lane = LaneForCase(corpusCase);
                Severity = SeverityForCase(corpusCase);
                Priority = PriorityForCase(corpusCase, matchScore);
                ScorePenalty = ScorePenaltyForCase(corpusCase, matchScore);
                Cause = string.IsNullOrEmpty(corpusCase.rootCause)
                    ? "Matched this log against a known VRChat creator failure pattern."
                    : corpusCase.rootCause;
                Fix = string.IsNullOrEmpty(corpusCase.fixFirst)
                    ? "Start with the matched root cause before changing unrelated assets."
                    : corpusCase.fixFirst;
                Recommendations = MergeRecommendations(corpusCase);
                Evidence = evidence;
                CaseType = string.IsNullOrEmpty(corpusCase.caseType) ? "unknown" : corpusCase.caseType;
                MatchStrictness = string.IsNullOrEmpty(corpusCase.matchStrictness) ? "strong" : corpusCase.matchStrictness;
                Confidence = Mathf.Min(96, 45 + Mathf.RoundToInt(matchScore / 2f));
            }

            public string Id { get; }
            public string Title { get; }
            public PriorityLane Lane { get; }
            public Severity Severity { get; }
            public int Priority { get; }
            public int ScorePenalty { get; }
            public string Cause { get; }
            public string Fix { get; }
            public string[] Recommendations { get; }
            public List<EvidenceLine> Evidence { get; }
            public string CaseType { get; }
            public string MatchStrictness { get; }
            public int Confidence { get; }

            private static PriorityLane LaneForCase(CorpusCase corpusCase)
            {
                if (corpusCase.caseType == "upload-support")
                {
                    return PriorityLane.UploadSupport;
                }

                if (corpusCase.severity == "blocker" &&
                    corpusCase.caseType == "console-error")
                {
                    return PriorityLane.FixFirst;
                }

                if (corpusCase.caseType == "warning")
                {
                    return PriorityLane.Warning;
                }

                if (corpusCase.caseType == "workflow" ||
                    corpusCase.caseType == "knowledge" ||
                    corpusCase.caseType == "ux")
                {
                    return PriorityLane.Related;
                }

                if (corpusCase.severity == "info")
                {
                    return PriorityLane.Later;
                }

                if (corpusCase.category == "optimization" ||
                    corpusCase.category == "visual-polish" ||
                    corpusCase.category == "creator-workflow")
                {
                    return PriorityLane.Later;
                }

                return PriorityLane.Then;
            }

            private static Severity SeverityForCase(CorpusCase corpusCase)
            {
                return corpusCase.severity == "blocker" ? Severity.Blocker : Severity.Warning;
            }

            private static int PriorityForCase(CorpusCase corpusCase, int matchScore)
            {
                var severityWeight = corpusCase.severity == "blocker" ? 100 : corpusCase.severity == "warning" ? 60 : 30;
                var caseTypeWeight = 0;
                switch (corpusCase.caseType)
                {
                    case "console-error":
                    case "upload-support":
                        caseTypeWeight = 40;
                        break;
                    case "runtime-behavior":
                        caseTypeWeight = 5;
                        break;
                    case "warning":
                        caseTypeWeight = -15;
                        break;
                    case "workflow":
                        caseTypeWeight = -45;
                        break;
                    case "knowledge":
                    case "ux":
                        caseTypeWeight = -35;
                        break;
                }

                var categoryWeight = 0;

                switch (corpusCase.category)
                {
                    case "upload-readiness":
                        categoryWeight = 18;
                        break;
                    case "build-export":
                        categoryWeight = 16;
                        break;
                    case "udon-compile":
                        categoryWeight = 14;
                        break;
                    case "udon-import":
                        categoryWeight = 12;
                        break;
                    case "network-sync":
                        categoryWeight = 8;
                        break;
                    case "interaction-wiring":
                        categoryWeight = 6;
                        break;
                    case "optimization":
                        categoryWeight = -8;
                        break;
                    case "creator-workflow":
                        categoryWeight = -10;
                        break;
                    case "visual-polish":
                        categoryWeight = -14;
                        break;
                    case "vrchat-knowledge":
                        categoryWeight = -6;
                        break;
                }

                return severityWeight + caseTypeWeight + categoryWeight + matchScore + corpusCase.priorityBoost;
            }

            private static int ScorePenaltyForCase(CorpusCase corpusCase, int matchScore)
            {
                if (corpusCase.caseType == "workflow" ||
                    corpusCase.caseType == "knowledge" ||
                    corpusCase.caseType == "ux")
                {
                    return 0;
                }

                var penalty = corpusCase.severity == "blocker" ? 14 : corpusCase.severity == "warning" ? 7 : 2;
                return Math.Min(20, penalty + Mathf.RoundToInt(matchScore / 18f));
            }

            private static string[] MergeRecommendations(CorpusCase corpusCase)
            {
                var merged = new List<string>();
                if (corpusCase.recommendations != null)
                {
                    merged.AddRange(corpusCase.recommendations);
                }

                if (corpusCase.vrchatSpecificNotes != null)
                {
                    merged.AddRange(corpusCase.vrchatSpecificNotes);
                }

                return merged.Take(2).ToArray();
            }
        }

        private static class SuspectAssetScanner
        {
            private static readonly string[] Extensions = { ".asset", ".prefab", ".unity" };

            private static readonly ScanRule[] Rules =
            {
                new ScanRule(new Regex(@"sourceCsScript:\s*\{fileID:\s*0\b", RegexOptions.IgnoreCase), "Missing UdonSharp source script reference."),
                new ScanRule(new Regex(@"serializedProgramAsset:\s*\{fileID:\s*0\b", RegexOptions.IgnoreCase), "Missing serialized Udon program asset reference."),
                new ScanRule(new Regex(@"programSource:\s*\{fileID:\s*0\b", RegexOptions.IgnoreCase), "Missing Udon program source reference."),
                new ScanRule(new Regex(@"m_Script:\s*\{fileID:\s*0\b", RegexOptions.IgnoreCase), "Missing script reference on a serialized Udon-related object.")
            };

            public static List<SuspectAsset> Scan()
            {
                var root = Application.dataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    return new List<SuspectAsset>();
                }

                var suspects = new List<SuspectAsset>();
                var seen = new HashSet<string>();

                foreach (var fullPath in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (!ShouldScan(fullPath))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(fullPath);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (!LooksUdonRelated(text))
                    {
                        continue;
                    }

                    var assetPath = ToAssetPath(root, fullPath);
                    foreach (var rule in Rules)
                    {
                        foreach (Match match in rule.Pattern.Matches(text))
                        {
                            var line = LineNumber(text, match.Index);
                            var context = FindNearestName(text, match.Index);
                            var key = assetPath + "|" + rule.Reason + "|" + context;
                            if (!seen.Add(key))
                            {
                                continue;
                            }

                            suspects.Add(new SuspectAsset(
                                assetPath,
                                rule.Reason + " Line " + line + ".",
                                context,
                                ClassifyPath(assetPath),
                                "Reference scan not run.",
                                string.Empty,
                                false,
                                0,
                                new string[0]));
                            if (suspects.Count >= 75)
                            {
                                return AddReferenceInfo(root, suspects);
                            }
                        }
                    }
                }

                suspects = suspects
                    .OrderBy(suspect => suspect.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(suspect => suspect.Reason, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return AddReferenceInfo(root, suspects);
            }

            private static bool ShouldScan(string fullPath)
            {
                var extension = Path.GetExtension(fullPath);
                return Extensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
            }

            private static bool LooksUdonRelated(string text)
            {
                return text.IndexOf("Udon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("UdonSharp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("VRC.Udon", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static string ToAssetPath(string root, string fullPath)
            {
                var relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return ("Assets/" + relative).Replace('\\', '/');
            }

            private static string ClassifyPath(string assetPath)
            {
                if (assetPath.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    assetPath.IndexOf("/PackageCache/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Third-party/package asset. Prefer reimporting or updating the package before editing this file directly.";
                }

                return "Project asset. Inspect references before removing or recreating it.";
            }

            private static List<SuspectAsset> AddReferenceInfo(string root, List<SuspectAsset> suspects)
            {
                if (suspects.Count == 0)
                {
                    return suspects;
                }

                var referenceFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(path => ShouldScan(path))
                    .ToList();
                var currentScenes = CurrentOpenScenePaths();

                var enriched = new List<SuspectAsset>();
                foreach (var suspect in suspects)
                {
                    var references = FindGuidReferences(root, referenceFiles, suspect.Path)
                        .Take(8)
                        .ToArray();
                    var isInCurrentScene = references.Any(reference => currentScenes.Contains(reference));
                    var summary = references.Length == 0
                        ? "No scene/prefab/asset GUID references found. It may be unused, dynamically loaded, or referenced through nested data."
                        : references.Length + " scene/prefab/asset reference" + (references.Length == 1 ? "" : "s") + " found.";
                    var nextAction = BuildNextAction(suspect.Path, references, isInCurrentScene);
                    var rank = RankSuspect(suspect.Path, references, isInCurrentScene);

                    enriched.Add(new SuspectAsset(
                        suspect.Path,
                        suspect.Reason,
                        suspect.Context,
                        suspect.Classification,
                        summary,
                        nextAction,
                        isInCurrentScene,
                        rank,
                        references));
                }

                return enriched
                    .OrderByDescending(suspect => suspect.Rank)
                    .ThenBy(suspect => suspect.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            private static HashSet<string> CurrentOpenScenePaths()
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    var scene = EditorSceneManager.GetSceneAt(i);
                    if (!string.IsNullOrEmpty(scene.path))
                    {
                        paths.Add(scene.path.Replace('\\', '/'));
                    }
                }

                return paths;
            }

            private static int RankSuspect(string path, string[] references, bool isInCurrentScene)
            {
                var rank = references.Length * 10;
                if (isInCurrentScene)
                {
                    rank += 120;
                }

                if (references.Any(reference => reference.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
                {
                    rank += 60;
                }

                if (references.Any(reference => reference.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                {
                    rank += 35;
                }

                if (path.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rank += 5;
                }

                return rank;
            }

            private static string BuildNextAction(string path, string[] references, bool isInCurrentScene)
            {
                var scene = references.FirstOrDefault(reference => reference.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
                if (isInCurrentScene && !string.IsNullOrEmpty(scene))
                {
                    return "Inspect the current open scene first: " + scene + ". This suspect is likely part of the world you are building now.";
                }

                if (!string.IsNullOrEmpty(scene))
                {
                    return "Inspect the owner scene first: " + scene + ". Remove/reimport the prefab instance there before editing package files.";
                }

                var prefab = references.FirstOrDefault(reference => reference.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(prefab))
                {
                    return "Inspect the owner prefab first: " + prefab + ". It likely pulls in this broken Udon asset.";
                }

                if (references.Length > 0)
                {
                    return "Inspect the first owner asset before changing the suspect: " + references[0] + ".";
                }

                if (path.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "No direct owner was found. Try reimporting/updating this third-party package before deleting package internals.";
                }

                return "No direct owner was found. Inspect this asset manually and rebuild after any change.";
            }

            private static IEnumerable<string> FindGuidReferences(string root, List<string> referenceFiles, string assetPath)
            {
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    yield break;
                }

                var fullSuspectPath = Path.GetFullPath(Path.Combine(root, assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
                foreach (var referenceFile in referenceFiles)
                {
                    if (string.Equals(Path.GetFullPath(referenceFile), fullSuspectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(referenceFile);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (text.IndexOf(guid, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    yield return ToAssetPath(root, referenceFile);
                }
            }

            private static int LineNumber(string text, int index)
            {
                var line = 1;
                for (var i = 0; i < index && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                    }
                }

                return line;
            }

            private static string FindNearestName(string text, int index)
            {
                var start = Math.Max(0, index - 1800);
                var length = index - start;
                if (length <= 0)
                {
                    return string.Empty;
                }

                var before = text.Substring(start, length);
                var matches = Regex.Matches(before, @"^\s*m_Name:\s*(.+)$", RegexOptions.Multiline);
                if (matches.Count == 0)
                {
                    return string.Empty;
                }

                for (var i = matches.Count - 1; i >= 0; i--)
                {
                    var value = matches[i].Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value) &&
                        value.IndexOf("m_EditorClassIdentifier", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !value.StartsWith("m_", StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }

            private readonly struct ScanRule
            {
                public ScanRule(Regex pattern, string reason)
                {
                    Pattern = pattern;
                    Reason = reason;
                }

                public Regex Pattern { get; }
                public string Reason { get; }
            }
        }

        private readonly struct SuspectAsset
        {
            public SuspectAsset(string path, string reason, string context, string classification, string referenceSummary, string nextAction, bool isInCurrentScene, int rank, string[] references)
            {
                Path = path;
                Reason = reason;
                Context = context;
                Classification = classification;
                ReferenceSummary = referenceSummary;
                NextAction = nextAction;
                IsInCurrentScene = isInCurrentScene;
                Rank = rank;
                References = references ?? new string[0];
            }

            public string Path { get; }
            public string Reason { get; }
            public string Context { get; }
            public string Classification { get; }
            public string ReferenceSummary { get; }
            public string NextAction { get; }
            public bool IsInCurrentScene { get; }
            public int Rank { get; }
            public string[] References { get; }
        }

        private readonly struct EvidenceLine
        {
            public EvidenceLine(string line, int count, string[] suspects)
            {
                Line = line;
                Count = count;
                Suspects = suspects ?? new string[0];
            }

            public string Line { get; }
            public int Count { get; }
            public string[] Suspects { get; }
        }

        private readonly struct ConsoleEntry
        {
            public ConsoleEntry(string firstLine, string text, ConsoleEntryKind kind)
            {
                FirstLine = firstLine;
                Text = text;
                Kind = kind;
            }

            public string FirstLine { get; }
            public string Text { get; }
            public ConsoleEntryKind Kind { get; }
            public bool IsError => Kind == ConsoleEntryKind.Error;
            public bool IsWarning => Kind == ConsoleEntryKind.Warning;
        }

        private enum ConsoleEntryKind
        {
            Log,
            Warning,
            Error
        }

        private sealed class AnalysisResult
        {
            public static readonly AnalysisResult Empty = new AnalysisResult(new List<Finding>(), 0, 0, 0, 0, 0, 0, "-");

            public AnalysisResult(List<Finding> findings, int rawLineCount, int errorCount, int rootCauseCount, int warningCount, int duplicateCount, int score, string uploadChance)
            {
                Findings = findings;
                RawLineCount = rawLineCount;
                ErrorCount = errorCount;
                RootCauseCount = rootCauseCount;
                WarningCount = warningCount;
                DuplicateCount = duplicateCount;
                Score = score;
                UploadChance = uploadChance;
            }

            public List<Finding> Findings { get; }
            public int RawLineCount { get; }
            public int ErrorCount { get; }
            public int RootCauseCount { get; }
            public int WarningCount { get; }
            public int DuplicateCount { get; }
            public int Score { get; }
            public string UploadChance { get; }
            public bool HasFindings => Findings.Count > 0;
        }

        private enum PriorityLane
        {
            UploadSupport,
            FixFirst,
            Then,
            Warning,
            Related,
            Later
        }

        private enum Severity
        {
            Blocker,
            Warning
        }
    }
}

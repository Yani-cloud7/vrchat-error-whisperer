const corpusUrl = "validation/errors/vrchat-error-corpus.json";

const sampleLog = `[Always] VRCSDK build requested for Windows
Build & Test succeeds but Build & Publish still fails
Upload debugger isolates the failure after Unity bundle creation
VRChat upload or API request fails even after local project checks pass
Creator had to contact VRChat support
Only VRChat could fix the account, world, blueprint, or backend state
Assertion failed on expression: 'success && actual == size'
Building AssetBundle failed
Building AssetBundle failed
UdonSharp compile failed: Assets/UdonSharp/UtilityScripts/RoleRegistrar.cs(88,2): error CS1513: } expected
UdonSharp compile failed: Assets/UdonSharp/UtilityScripts/RoleRegistrar.cs(88,2): error CS1513: } expected
`;

const laneCopy = {
  support: { title: "Upload/support escalation", detail: "VRChat-side, account, blueprint, or upload API issues." },
  fix: { title: "Fix first", detail: "Root causes that can block the build or make other errors meaningless." },
  then: { title: "Then", detail: "Important issues after the build has a clean path forward." },
  warning: { title: "Warnings", detail: "Real warnings, but not build blockers." },
  related: { title: "Known related issues", detail: "Historical or workflow matches. Use as context, not proof." },
  later: { title: "Ignore until later", detail: "Real issues, but usually not the first thing to touch." }
};

const noisePatterns = [
  /begin mono manager reload/i,
  /refreshing native plugins/i,
  /assetdatabase/i,
  /domain reload/i,
  /repaint/i,
  /layout rebuild/i
];

const stopWords = new Set([
  "the", "and", "that", "this", "with", "from", "into", "your", "you", "are", "was", "were",
  "but", "not", "can", "cannot", "after", "before", "then", "than", "only", "even", "still",
  "when", "where", "while", "because", "unity", "vrchat", "build", "error", "failed", "fails"
]);

const logInput = document.querySelector("#logInput");
const analyzeButton = document.querySelector("#analyzeButton");
const loadSample = document.querySelector("#loadSample");
const clearLog = document.querySelector("#clearLog");
const questMode = document.querySelector("#questMode");
const findingsEl = document.querySelector("#findings");
const findingCount = document.querySelector("#findingCount");
const likelyCause = document.querySelector("#likelyCause");
const nextAction = document.querySelector("#nextAction");
const confidence = document.querySelector("#confidence");
const statusSignal = document.querySelector("#statusSignal");
const statusText = document.querySelector("#statusText");
const readinessScore = document.querySelector("#readinessScore");
const uploadChance = document.querySelector("#uploadChance");
const consoleSummary = document.querySelector("#consoleSummary");

let corpusCases = [];
let corpusError = "";

function normalizeLine(line) {
  return line
    .replace(/\(\d+,\d+\)/g, "(line,column)")
    .replace(/\bline \d+\b/gi, "line n")
    .replace(/\s+/g, " ")
    .trim();
}

function parseConsole(text) {
  const rawLines = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const usefulLines = rawLines.filter((line) => !noisePatterns.some((pattern) => pattern.test(line)));
  const errorLines = usefulLines.filter(isErrorLine);
  const warningLines = usefulLines.filter(isWarningLine);
  const groups = new Map();
  const errorGroups = new Map();

  usefulLines.forEach((line) => {
    const key = normalizeLine(line);
    const existing = groups.get(key) || { line, count: 0 };
    existing.count += 1;
    groups.set(key, existing);
  });

  errorLines.forEach((line) => {
    const key = normalizeLine(line);
    const existing = errorGroups.get(key) || { line, count: 0 };
    existing.count += 1;
    errorGroups.set(key, existing);
  });

  return {
    rawCount: rawLines.length,
    usefulCount: usefulLines.length,
    errorCount: errorLines.length,
    warningCount: warningLines.length,
    errorText: errorLines.join("\n"),
    duplicateCount: usefulLines.length - groups.size,
    groups: [...groups.values()],
    errorGroups: [...errorGroups.values()]
  };
}

function isErrorLine(line) {
  return /^\[Error\]|^\[Assert\]|^\[Fatal\]|\berror\s+CS\d{4}\b|\bException\b|UnityException|AssetBundle was not built|Building AssetBundle failed|Upload failed|Build failed|\bfails?\b|\bfailure\b/i.test(line || "");
}

function isWarningLine(line) {
  return /^\[Warning\]|\bwarning\s+CS\d{4}\b|obsolete|deprecated/i.test(line || "");
}

async function loadCorpus() {
  try {
    const response = await fetch(corpusUrl, { cache: "no-store" });
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    const data = await response.json();
    corpusCases = Array.isArray(data.cases) ? data.cases : [];
    corpusError = "";
  } catch (error) {
    corpusCases = [];
    corpusError = `Could not load ${corpusUrl}: ${error.message}`;
  }
}

function tokenize(value) {
  return (value || "")
    .toLowerCase()
    .match(/[a-z0-9_./#-]{3,}/g)
    ?.filter((token) => !stopWords.has(token))
    .filter((token, index, tokens) => tokens.indexOf(token) === index) || [];
}

function scoreSignal(signal, textLower) {
  const signalLower = (signal || "").toLowerCase().trim();
  if (!signalLower) return 0;
  if (textLower.includes(signalLower)) return 100;

  const tokens = tokenize(signalLower);
  if (tokens.length === 0) return 0;

  const hits = tokens.filter((token) => textLower.includes(token)).length;
  const ratio = hits / tokens.length;
  if (tokens.length <= 2) return 0;
  if (ratio >= 0.85 && hits >= 3) return 55 + Math.round(ratio * 25);
  if (ratio >= 0.65 && hits >= 4) return 30 + Math.round(ratio * 20);
  return 0;
}

function scoreLineAgainstSignal(line, signal) {
  const lineLower = line.toLowerCase();
  const signalLower = (signal || "").toLowerCase().trim();
  if (!signalLower) return 0;
  if (lineLower.includes(signalLower) || signalLower.includes(lineLower)) return 100;

  const tokens = tokenize(signalLower);
  if (tokens.length === 0) return 0;
  const hits = tokens.filter((token) => lineLower.includes(token)).length;
  const ratio = hits / tokens.length;
  return ratio >= 0.65 && hits >= 3 ? Math.round(ratio * 80) : 0;
}

function regexMatches(pattern, value) {
  try {
    return new RegExp(pattern, "i").test(value || "");
  } catch {
    return false;
  }
}

function shouldSkipForTarget(testCase) {
  if (questMode.checked) return false;
  const haystack = `${testCase.title || ""} ${(testCase.rawSignals || []).join(" ")}`.toLowerCase();
  return /\bquest\b|android/.test(haystack);
}

function laneForCase(testCase) {
  if (testCase.caseType === "upload-support") return "support";
  if (testCase.severity === "blocker" && testCase.caseType === "console-error") return "fix";
  if (testCase.caseType === "warning") return "warning";
  if (["workflow", "knowledge", "ux"].includes(testCase.caseType)) return "related";
  if (testCase.severity === "info") return "later";
  if (["optimization", "visual-polish", "creator-workflow"].includes(testCase.category)) return "later";
  return "then";
}

function severityForCase(testCase) {
  if (testCase.severity === "blocker") return "severe";
  if (testCase.severity === "warning") return "warn";
  return "good";
}

function priorityForCase(testCase, score) {
  const severityWeight = testCase.severity === "blocker" ? 100 : testCase.severity === "warning" ? 60 : 30;
  const caseTypeWeight = {
    "console-error": 40,
    "upload-support": 40,
    "runtime-behavior": 5,
    "warning": -15,
    "workflow": -45,
    "knowledge": -35,
    "ux": -35
  }[testCase.caseType] || 0;
  const categoryWeight = {
    "upload-readiness": 18,
    "build-export": 16,
    "udon-compile": 14,
    "udon-import": 12,
    "network-sync": 8,
    "interaction-wiring": 6,
    "optimization": -8,
    "creator-workflow": -10,
    "visual-polish": -14,
    "vrchat-knowledge": -6
  }[testCase.category] || 0;

  return severityWeight + caseTypeWeight + categoryWeight + score + (testCase.priorityBoost || 0);
}

function scorePenaltyForCase(testCase, score) {
  if (["workflow", "knowledge", "ux"].includes(testCase.caseType)) return 0;
  const base = testCase.severity === "blocker" ? 14 : testCase.severity === "warning" ? 7 : 2;
  return Math.min(20, base + Math.round(score / 18));
}

function passesStrictness(testCase, matchedSignals, totalScore) {
  const strictness = testCase.matchStrictness || "strong";
  if (strictness === "exact") return matchedSignals.some((item) => item.score >= 100);
  if (strictness === "strong") return totalScore >= 65;
  return totalScore >= 80;
}

function matchCase(testCase, textLower, consoleInfo) {
  if (shouldSkipForTarget(testCase)) return null;

  const signals = Array.isArray(testCase.rawSignals) ? testCase.rawSignals : [];
  const regexSignals = Array.isArray(testCase.regexSignals) ? testCase.regexSignals : [];
  const signalScores = signals.map((signal) => ({ signal, score: scoreSignal(signal, textLower) }));
  const regexScores = regexSignals.map((signal) => ({ signal, score: regexMatches(signal, textLower) ? 100 : 0, regex: true }));
  const matchedSignals = [...signalScores, ...regexScores].filter((item) => item.score > 0);
  if (!matchedSignals.length) return null;

  const bestScore = Math.max(...matchedSignals.map((item) => item.score));
  const totalScore = Math.min(100, bestScore + Math.max(0, matchedSignals.length - 1) * 12);
  if (!passesStrictness(testCase, matchedSignals, totalScore)) return null;

  const evidence = [];
  for (const group of consoleInfo.errorGroups) {
    const rawEvidenceScore = signals.length
      ? Math.max(...signals.map((signal) => scoreLineAgainstSignal(group.line, signal)))
      : 0;
    const regexEvidenceScore = regexSignals.some((signal) => regexMatches(signal, group.line)) ? 100 : 0;
    const evidenceScore = Math.max(rawEvidenceScore, regexEvidenceScore);
    if (evidenceScore > 0) evidence.push({ ...group, evidenceScore });
  }

  evidence.sort((a, b) => b.evidenceScore - a.evidenceScore);

  return {
    id: testCase.id,
    lane: laneForCase(testCase),
    severity: severityForCase(testCase),
    priority: priorityForCase(testCase, totalScore),
    scorePenalty: scorePenaltyForCase(testCase, totalScore),
    title: testCase.title,
    cause: testCase.rootCause || "Matched this log against a known VRChat creator failure pattern.",
    fix: testCase.fixFirst || "Start with the matched root cause before changing unrelated assets.",
    recommendations: [...(testCase.recommendations || []), ...(testCase.vrchatSpecificNotes || [])].slice(0, 5),
    confidence: Math.min(96, 45 + Math.round(totalScore / 2)),
    evidence: evidence.slice(0, 2),
    matchedSignals: matchedSignals.map((item) => item.signal),
    caseType: testCase.caseType || "unknown",
    matchStrictness: testCase.matchStrictness || "strong"
  };
}

function analyzeLog() {
  const text = logInput.value.trim();
  const consoleInfo = parseConsole(text);

  if (consoleInfo.rawCount && consoleInfo.errorCount === 0) {
    renderAnalysis([{
      id: "warnings-only",
      lane: "fix",
      severity: "good",
      priority: 0,
      scorePenalty: 0,
      title: "0 errors found",
      cause: "No current build or upload blockers were detected. Warnings are being ignored for blocker ranking.",
      fix: "You do not need to fix anything first from this console snapshot.",
      recommendations: [],
      confidence: 95,
      evidence: []
    }], consoleInfo);
    return;
  }

  if (corpusError) {
    renderAnalysis([{
      id: "corpus-error",
      lane: "fix",
      severity: "warn",
      priority: 0,
      scorePenalty: 0,
      title: "Corpus could not be loaded",
      cause: corpusError,
      fix: "Run the prototype from a local web server so fetch can load the JSON corpus.",
      recommendations: ["Example: use a static server from the project root, then open the localhost URL."],
      confidence: 0,
      evidence: []
    }], consoleInfo);
    return;
  }

  if (!corpusCases.length) {
    renderAnalysis([{
      id: "loading",
      lane: "fix",
      severity: "good",
      priority: 0,
      scorePenalty: 0,
      title: "Loading corpus",
      cause: "The VRChat creator corpus is still loading.",
      fix: "Analysis will run automatically once the corpus is ready.",
      recommendations: [],
      confidence: 0,
      evidence: []
    }], consoleInfo);
    return;
  }

  const textLower = consoleInfo.errorText.toLowerCase();
  const activeFindings = corpusCases
    .map((testCase) => matchCase(testCase, textLower, consoleInfo))
    .filter(Boolean)
    .sort((a, b) => b.priority - a.priority)
    .slice(0, 8);

  const findings = activeFindings.length ? activeFindings : [{
    id: "clean",
    lane: "fix",
    severity: "good",
    priority: 0,
    scorePenalty: 0,
    title: "No known VRChat creator pattern detected",
    cause: "The pasted output did not match the current seed corpus.",
    fix: "Check the earliest red Unity console entry, then add that exact line to the corpus as a new rawSignal.",
    recommendations: ["Paste the full Unity Console output instead of a single line when possible."],
    confidence: text ? 42 : 0,
    evidence: []
  }];

  renderAnalysis(findings, consoleInfo);
}

function renderAnalysis(findings, consoleInfo) {
  findingsEl.innerHTML = "";

  const realFindings = findings.filter((finding) => !["clean", "loading", "corpus-error"].includes(finding.id));
  const score = Math.max(0, 100 - realFindings.reduce((total, finding) => total + finding.scorePenalty, 0));
  const chance = score >= 82 ? "High" : score >= 58 ? "Medium" : "Low";
  const rootCount = realFindings.length || 0;

  readinessScore.textContent = realFindings.length ? `${score}/100` : "-";
  uploadChance.textContent = realFindings.length ? chance : "-";
  findingCount.textContent = String(realFindings.length || findings.length);
  consoleSummary.innerHTML = `
    <div><strong>${consoleInfo.rawCount}</strong> console lines pasted</div>
    <div><strong>${consoleInfo.errorCount}</strong> error line${consoleInfo.errorCount === 1 ? "" : "s"}</div>
    <div><strong>${consoleInfo.warningCount}</strong> warning line${consoleInfo.warningCount === 1 ? "" : "s"}</div>
    <div><strong>${rootCount || findings.length}</strong> root cause${(rootCount || findings.length) === 1 ? "" : "s"} detected</div>
    <div><strong>${consoleInfo.duplicateCount}</strong> duplicate/noise line${consoleInfo.duplicateCount === 1 ? "" : "s"} collapsed</div>
  `;

  Object.keys(laneCopy).forEach((lane) => {
    const laneFindings = findings.filter((finding) => finding.lane === lane);
    if (!laneFindings.length) return;

    const group = document.createElement("section");
    group.className = "priority-group";
    group.innerHTML = `
      <div class="priority-title">
        ${laneCopy[lane].title}
        <span>${laneCopy[lane].detail}</span>
      </div>
    `;

    laneFindings.forEach((finding) => group.append(createFindingCard(finding)));
    findingsEl.append(group);
  });

  const primary = findings[0];
  likelyCause.textContent = primary.cause;
  nextAction.textContent = primary.fix;
  confidence.textContent = primary.confidence ? `${primary.confidence}%` : "-";
  statusSignal.className = `signal ${primary.severity === "good" ? "" : primary.severity}`;
  statusText.textContent = primary.severity === "good"
    ? "No blocker matched"
    : `${rootCount || findings.length} root cause${(rootCount || findings.length) === 1 ? "" : "s"} detected`;
}

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

function createFindingCard(finding) {
  const card = document.createElement("article");
  card.className = `finding ${finding.severity}`;
  const evidence = finding.evidence.length
    ? `<div class="evidence">Evidence: ${finding.evidence.map((item) => `${escapeHtml(item.line)}${item.count > 1 ? ` (${item.count}x)` : ""}`).join(" | ")}</div>`
    : "";
  const meta = finding.caseType
    ? `<div class="evidence">Match: ${escapeHtml(finding.caseType)} / ${escapeHtml(finding.matchStrictness)} / ${escapeHtml(finding.confidence)}%</div>`
    : "";
  const recommendations = finding.recommendations.length
    ? `<div class="recommendations"><strong>Recommended:</strong><ul>${finding.recommendations.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul></div>`
    : "";

  card.innerHTML = `
    <div class="finding-head">
      <h3>${escapeHtml(finding.title)}</h3>
      <span class="severity">${escapeHtml(finding.severity)}</span>
    </div>
    <p>${escapeHtml(finding.cause)}</p>
    <div class="fix">${escapeHtml(finding.fix)}</div>
    ${recommendations}
    ${meta}
    ${evidence}
  `;
  return card;
}

loadSample.addEventListener("click", () => {
  logInput.value = sampleLog;
  analyzeLog();
});

clearLog.addEventListener("click", () => {
  logInput.value = "";
  analyzeLog();
  logInput.focus();
});

analyzeButton.addEventListener("click", analyzeLog);
questMode.addEventListener("change", analyzeLog);
logInput.addEventListener("input", () => {
  if (logInput.value.length < 2) analyzeLog();
});

logInput.value = sampleLog;
analyzeLog();
loadCorpus().then(analyzeLog);

# Alpha Testing

VRChat Error Whisperer is an alpha advisory tool. It does not modify Unity projects. The goal is to help creators ignore noisy console spam and fix the first meaningful blocker.

## Invite Message

```text
I am alpha testing a Unity editor tool for VRChat creators.
It reads Unity Console / VRChat SDK output and tries to tell you what to fix first.

VCC repository URL:
https://yani-cloud7.github.io/vrchat-error-whisperer/index.json

If it gives a wrong or missing result, please send:
- screenshot of the Error Whisperer window
- pasted Unity Console output
- what you expected it to say
- what actually fixed the issue, if you know

Please remove private world IDs, account names, and secret project paths before sharing logs publicly.
```

## Tester Checklist

- Install from VCC using the repository URL.
- Confirm `Tools > VRChat Utility > Error Whisperer` appears in Unity.
- Try it after a real failed SDK build, UdonSharp compile, Quest build, or upload.
- Report wrong or missing results using the GitHub issue templates.


from pathlib import Path
import re


repo_root = Path(__file__).resolve().parents[1]
source_path = repo_root / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs"
source = source_path.read_text(encoding="utf-8-sig")


required_markers = (
    'private const string CanonicalCoreName = "PAL98UNHAPPY";',
    'private const string LegacyCoreName = "仙剑98DX9不欢乐模式";',
    "CoreName = CanonicalCoreName;",
    'string sourceBestFile = "best" + LegacyCoreName + ".txt";',
    'string targetBestFile = "best" + CanonicalCoreName + ".txt";',
    "File.Exists(sourceBestFile) && !File.Exists(targetBestFile)",
    "File.Copy(sourceBestFile, targetBestFile);",
    "base.LoadPlugins();",
    'sn.StartsWith("PAL98.") && sn.EndsWith(".tpg")',
)

for marker in required_markers:
    if marker not in source:
        raise AssertionError(f"missing PAL98UNHAPPY identity marker: {marker}")

if re.search(r'^\s*CoreName\s*=\s*"仙剑98DX9不欢乐模式";', source, re.MULTILINE):
    raise AssertionError("legacy Chinese CoreName assignment must not remain active")

print("PASS: PAL98UNHAPPY uses the canonical identity and preserves legacy local best data.")

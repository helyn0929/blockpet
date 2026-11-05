#!/usr/bin/env bash
# BlockPet：用 batchmode 跑 EditMode 測試。
# 結果存成 XML（Logs/TestResults/editmode-results.xml），最後印出通過／失敗數量和失敗的測試。
# Unity 編輯器開著這個專案時不能跑（同一個專案只能有一個 Unity），會提示先關閉。
#
# 用法：scripts/run-tests.sh
# 換 Unity 路徑：UNITY_PATH=/path/to/Unity scripts/run-tests.sh
# 回傳值：0 全部通過；1 有測試失敗；2 沒跑起來（編譯錯誤、Unity 開著等）

set -u

PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
OUT="$PROJECT/Logs/TestResults"
XML="$OUT/editmode-results.xml"
LOG="$OUT/editmode.log"

if [ ! -x "$UNITY" ]; then
  echo "找不到 Unity $VERSION：$UNITY"
  echo "請用 Unity Hub 安裝這個版本，或設定 UNITY_PATH。"
  exit 2
fi

# ─── Unity 編輯器是不是正開著這個專案 ───────────────────────────────
# 1) 有 Unity 程序的參數指到這個專案；2) Temp/UnityLockfile 被某個程序開著
open_pids="$(ps -axo pid=,command= | grep -i "Unity.app/Contents/MacOS/Unity" | grep -v grep \
  | grep -iF -- "$PROJECT" | grep -v -- "-batchmode" | awk '{print $1}')"
if [ -z "$open_pids" ] && [ -f "$PROJECT/Temp/UnityLockfile" ] && command -v lsof >/dev/null 2>&1; then
  open_pids="$(lsof -t "$PROJECT/Temp/UnityLockfile" 2>/dev/null)"
fi
if [ -n "$open_pids" ]; then
  echo "Unity 編輯器正開著這個專案（PID：$(echo $open_pids)）。"
  echo "請先關閉 Unity，再重新執行 scripts/run-tests.sh。"
  exit 2
fi

mkdir -p "$OUT"
rm -f "$XML"

echo "Unity $VERSION：執行 EditMode 測試…（第一次會比較久，記錄在 Logs/TestResults/editmode.log）"
"$UNITY" -batchmode -nographics \
  -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$XML" \
  -logFile "$LOG"
code=$?

if [ ! -f "$XML" ]; then
  echo ""
  echo "測試沒有跑起來（Unity 結束碼 $code），沒有產生結果檔。"
  echo "常見原因：編譯錯誤、授權問題。Log 最後幾行："
  echo "──────────"
  grep -E "error CS|Error|error" "$LOG" | tail -n 15
  echo "──────────"
  echo "完整記錄：$LOG"
  exit 2
fi

# ─── 讀結果 XML（NUnit 3 格式）──────────────────────────────────────
python3 - "$XML" <<'PY'
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
cases = root.iter("test-case")
passed, failed, skipped = 0, [], 0
for c in cases:
    r = c.get("result", "")
    if r == "Passed":
        passed += 1
    elif r == "Failed":
        msg = (c.findtext("failure/message") or "").strip().splitlines()
        failed.append((c.get("fullname") or c.get("name"), msg[0] if msg else ""))
    else:
        skipped += 1

print("")
print(f"通過：{passed}　失敗：{len(failed)}　略過：{skipped}")
if failed:
    print("失敗的測試：")
    for name, msg in failed:
        print(f"  ✗ {name}")
        if msg:
            print(f"      {msg}")
print(f"結果檔：{sys.argv[1]}")
sys.exit(1 if failed else 0)
PY

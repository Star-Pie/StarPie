import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

content = open('WinPieGestures/I18n.cs', encoding='utf-8').read()
pattern = re.compile(r'Add\("([^"]+)",\s*"([^"]*)",\s*"([^"]*)",\s*"([^"]*)",\s*"([^"]*)"\);')

matches = pattern.findall(content)
print(f"Total keys found: {len(matches)}")

errors = 0
checked = 0
for key, zh, tw, en, ja in matches:
    if 'Onboarding' in key or 'OfficialPlugin' in key:
        checked += 1
        ph_zh = sorted(re.findall(r'\{(\d+)\}', zh))
        ph_tw = sorted(re.findall(r'\{(\d+)\}', tw))
        ph_en = sorted(re.findall(r'\{(\d+)\}', en))
        ph_ja = sorted(re.findall(r'\{(\d+)\}', ja))
        if not (ph_zh == ph_tw == ph_en == ph_ja):
            print(f"Placeholder mismatch in {key}: zh={ph_zh}, tw={ph_tw}, en={ph_en}, ja={ph_ja}")
            errors += 1

print(f"Checked {checked} onboarding keys. Errors: {errors}")
if errors > 0:
    sys.exit(1)

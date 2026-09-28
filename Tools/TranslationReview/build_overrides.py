"""Build LocalizationOverrides.json: corrections applied on top of LocalizationData.json by
the plugin at load time (so an automatic update of the data does not wipe them out).

Rules, each recorded per change in report/changes.tsv:
- official-for-same-japanese: a machine-translated string whose Japanese is identical to a
  string the official global release translated gets that official English. The official
  pool is the mod's imports of the global release and the non-machine menu text; Manual
  edits by the mod author are never touched.
- hand-translated: strings still in Japanese, translated in translations/*.tsv.
- mod-translation-of-same-japanese: strings still in Japanese where the mod already has a
  translation of the identical Japanese elsewhere.
- consistency: 後編 is "Part 3" in events that also have a 中編; 復刻 is "Rerun".
- restore-game-identifier: tracking_id values are not text; the game's own values are kept.
- hand-translated-new-line: lines the game has but the data lacks (translations/new-textasset-lines.tsv).
Every candidate must keep the Japanese string's placeholders and line breaks.

Inputs:  jp/<table>.json (the game's Japanese master data, from extract/),
         Localization/ of this repo, translations/
Outputs: AtelierResleriana.Plugin.Localization/Resources/LocalizationOverrides.json,
         report/changes.tsv, report/untranslated.tsv
"""
import json, os, re, collections

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
REPO_LOC = os.path.join(REPO, 'Localization')
DATA = json.load(open(os.path.join(REPO_LOC, 'LocalizationData.json'), encoding='utf8'))
OVERRIDES = os.path.join(REPO, 'AtelierResleriana.Plugin.Localization', 'Resources', 'LocalizationOverrides.json')
JAPANESE = re.compile(r'[぀-ヺー-ヿ㐀-鿿]')  # kana/kanji, not the ・ bullet
UI_ASSETS = ('SystemText', 'BuiltinText', 'ErrorText')

def load(p): return json.load(open(p, encoding='utf-8-sig'))
def is_text(s): return isinstance(s, str) and bool(JAPANESE.search(s))
def usable(en): return isinstance(en, str) and en.strip() and not JAPANESE.search(en) and en.strip() != 'No Text'

def ids_in(folder):
    out = {}
    if os.path.isdir(folder):
        for f in os.listdir(folder):
            if 'PackedText' in folder and f[:-5] not in UI_ASSETS: continue
            out[f[:-5]] = {r['id'] for r in load(os.path.join(folder, f)) if isinstance(r, dict) and 'id' in r}
    return out

machine_md = ids_in(os.path.join(REPO_LOC, 'MasterData', 'Machine', 'en'))
manual_md = ids_in(os.path.join(REPO_LOC, 'MasterData', 'Manual', 'en'))
machine_ui = {a: ids for a, ids in ids_in(os.path.join(REPO_LOC, 'PackedTextEntryLocalization', 'Machine')).items() if a in UI_ASSETS}
manual_ui = {a: ids for a, ids in ids_in(os.path.join(REPO_LOC, 'PackedTextEntryLocalization', 'Manual')).items() if a in UI_ASSETS}

# Every translated string: (kind, table, id, field, japanese, english, is_machine)
strings = []
mod_md = DATA['masterDataLocalizationData']['en']
for table, rows in mod_md.items():
    jp_path = os.path.join(HERE, 'jp', table + '.json')
    if not isinstance(rows, list) or not os.path.exists(jp_path): continue
    jp = {r['id']: r for r in load(jp_path) if isinstance(r, dict) and 'id' in r}
    for r in rows:
        src = jp.get(r.get('id'))
        if not src: continue
        for field, en in r.items():
            if field != 'id' and isinstance(en, str) and is_text(src.get(field)):
                machine = r['id'] in machine_md.get(table, ()) and r['id'] not in manual_md.get(table, ())
                strings.append(('master', table, r['id'], field, src[field], en, machine))
for asset in UI_ASSETS:
    for e in DATA['textAssetLocalizationData'].get(asset, []):
        for pid, p in e['properties'].items():
            ja, en = p.get('text') or '', (p.get('localizations') or {}).get('en')
            if is_text(ja) and isinstance(en, str):
                machine = e['id'] in machine_ui.get(asset, ()) and e['id'] not in manual_ui.get(asset, ())
                strings.append(('ui', asset, e['id'], pid, ja, en, machine))

official = collections.defaultdict(collections.Counter)
for kind, table, rid, field, ja, en, machine in strings:
    if not machine and usable(en):
        official[ja][en] += 1

overrides = {'baseVersion': DATA['version'], 'masterData': {'en': {}}, 'textAssets': {'en': {}}}
# Any existing English for a Japanese string (machine included), for strings the mod left in Japanese.
anywhere = collections.defaultdict(collections.Counter)
for kind, table, rid, field, ja, en, machine in strings:
    if usable(en):
        anywhere[ja][en] += 1

# Hand translations (translations/*.tsv: japanese <tab> english, "\n" written as \n).
manual = {}
tdir = os.path.join(HERE, 'translations')
if os.path.isdir(tdir):
    for f in sorted(os.listdir(tdir)):
        if f.endswith('.tsv'):
            for line in open(os.path.join(tdir, f), encoding='utf-8-sig').read().split('\n'):
                if line.strip() and not line.startswith('#'):
                    ja, en = line.split('\t')[:2]
                    manual[ja.replace('\\n', '\n')] = en.replace('\\n', '\n')

# The game's own placeholders, shown the same way to Japanese players.
INTERNAL = re.compile(r'^(テキストなし|.*用|【.*用】)$')
PLACEHOLDER = re.compile(r'\{[^}]*\}|<[^>]+>')
def same_placeholders(a, b): return sorted(PLACEHOLDER.findall(a)) == sorted(PLACEHOLDER.findall(b))

# Events with a 中編 (middle part), i.e. three parts, found anywhere in the Japanese text.
THREE_PART_EVENTS = {m.group(1) for s in strings for m in re.finditer(r'([^\[「【\s]+?)[\s　]*中編', s[4])}

changes, untranslated = [], []
for kind, table, rid, field, ja, en, machine in strings:
    left_in_japanese = bool(JAPANESE.search(en))
    candidates = []
    if machine or left_in_japanese:
        candidates += [(t, 'official-for-same-japanese') for t, _ in official[ja].most_common()] if ja in official else []
    if left_in_japanese and not INTERNAL.match(ja.strip()):
        if ja in manual: candidates.append((manual[ja], 'hand-translated'))
        candidates += [(t, 'mod-translation-of-same-japanese') for t, _ in anywhere[ja].most_common()] if ja in anywhere else []
    # First candidate that keeps every placeholder and line break; otherwise leave as is.
    best, rule = next(((t, r) for t, r in candidates if same_placeholders(ja, t)), (None, None))
    if best is None and left_in_japanese and not INTERNAL.match(ja.strip()):
        untranslated.append((kind, table, rid, field, ja, en))
    # Consistency rules from the official text, applied to whatever English we end up with.
    result = best if best is not None else en
    if not JAPANESE.search(result):
        fixed = result
        # 後編 is "Part 3" in events that also have a 中編 (official: "Facility Coin [Yumia Festival Part 3]").
        m = re.search(r'([^\[「【\s]+?)[\s　]*後編', ja)
        if m and m.group(1) in THREE_PART_EVENTS and fixed.count('Part 2') == 1 and 'Part 3' not in fixed:
            fixed = fixed.replace('Part 2', 'Part 3')
        # 復刻 (a rerun banner or its ticket) is "Rerun"; the mod named them exactly like the originals.
        if ja.startswith('復刻') and not re.search(r'(?i)rerun|revival|reprint|encore|repeat|re-run', fixed) and table in ('item', 'gacha', 'gacha_step_up_button'):
            fixed = fixed.replace('make a wish in the ', 'make a wish in the Rerun ', 1) if 'make a wish in the ' in fixed else 'Rerun ' + fixed
        if fixed != result:
            best, rule = fixed, (rule + '+' if rule else '') + 'consistency'
    if best is None or best == en:
        continue
    section = overrides['masterData']['en'] if kind == 'master' else overrides['textAssets']['en']
    section.setdefault(table, {}).setdefault(str(rid), {})[field] = best
    changes.append((kind, table, rid, field, rule, ja, en, best))

# Identifier fields are not text: the mod imported the global release's analytics event names
# over the Japanese game's own tracking tokens (e.g. "9vjvuk" -> "af_clear_story_ch_0_14").
# Put the game's values back.
IDENTIFIER_FIELDS = {'tracking_id'}
for table, rows in mod_md.items():
    jp_path = os.path.join(HERE, 'jp', table + '.json')
    if not isinstance(rows, list) or not os.path.exists(jp_path): continue
    jp = {r['id']: r for r in load(jp_path) if isinstance(r, dict) and 'id' in r}
    for r in rows:
        src = jp.get(r.get('id'))
        for field in IDENTIFIER_FIELDS & r.keys():
            if src and isinstance(src.get(field), str) and r[field] != src[field]:
                overrides['masterData']['en'].setdefault(table, {}).setdefault(str(r['id']), {})[field] = src[field]
                changes.append(('master', table, r['id'], field, 'restore-game-identifier', src[field], r[field], src[field]))

# Lines the game's text assets have but the mod's data lacks entirely (found by story/).
new_lines = os.path.join(HERE, 'translations', 'new-textasset-lines.tsv')
if os.path.exists(new_lines):
    for line in open(new_lines, encoding='utf-8-sig').read().split('\n'):
        if not line.strip() or line.startswith('#'): continue
        asset, eid, pid, text = line.split('\t')
        text = text.replace('\\n', '\n')
        overrides['textAssets']['en'].setdefault(asset, {}).setdefault(eid, {})[pid] = text
        changes.append(('ui-new', asset, eid, pid, 'hand-translated-new-line', '', '', text))

os.makedirs(os.path.join(HERE, 'report'), exist_ok=True)
with open(OVERRIDES, 'w', encoding='utf8') as w:
    json.dump(overrides, w, ensure_ascii=False, indent=1, sort_keys=True)

def tsv(name, header, rows):
    with open(os.path.join(HERE, 'report', name), 'w', encoding='utf8') as w:
        w.write('\t'.join(header) + '\n')
        for r in rows: w.write('\t'.join(str(x).replace('\t', ' ').replace('\r', '').replace('\n', '\\n') for x in r) + '\n')
tsv('changes.tsv', ('kind', 'table', 'id', 'field', 'rule', 'japanese', 'before', 'after'), changes)
tsv('untranslated.tsv', ('kind', 'table', 'id', 'field', 'japanese', 'current'), untranslated)

by = collections.Counter((c[0], c[1]) for c in changes)
print(f'{len(strings)} strings, {sum(1 for s in strings if s[6])} machine; {len(changes)} changed, {len(untranslated)} still Japanese')
for (k, t), n in by.most_common(25): print(f'  {k:6s} {t:30s} {n}')

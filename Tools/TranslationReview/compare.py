"""Compare the English mod's game-data translations with the official English
(global release, as published by Barrel Wisdom), aligned by the Japanese source text.

Inputs:  jp/<table>.json               Japanese game data (extract/)
         barrelwisdom/<kind>.<ja|en>.json
         Localization/LocalizationData.json and Localization/MasterData/Machine/ of this repo
Output:  report/summary.txt, report/<table>.tsv
"""
import json, os, re, collections, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
MOD_DATA = os.path.join(REPO, 'Localization', 'LocalizationData.json')
MACHINE = os.path.join(REPO, 'Localization', 'MasterData', 'Machine', 'en')
JAPANESE = re.compile(r'[぀-ヿ㐀-鿿]')

def load(p): return json.load(open(p, encoding='utf8'))

# Official pairs: walk each Barrel Wisdom ja/en list in parallel; every string that differs
# and whose English side has no Japanese left in it is an official (ja -> en) pair.
official = collections.defaultdict(collections.Counter)
def walk(a, b):
    if isinstance(a, dict) and isinstance(b, dict):
        for k in a.keys() & b.keys(): walk(a[k], b[k])
    elif isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        for x, y in zip(a, b): walk(x, y)
    elif isinstance(a, str) and isinstance(b, str) and JAPANESE.search(a) and not JAPANESE.search(b) and b.strip():
        official[a.strip()][b.strip()] += 1
bw = os.path.join(HERE, 'barrelwisdom')
for f in sorted(os.listdir(bw)):
    if f.endswith('.ja.json'):
        walk(load(os.path.join(bw, f)), load(os.path.join(bw, f.replace('.ja.', '.en.'))))
official = {ja: c.most_common(1)[0][0] for ja, c in official.items()}

mod = load(MOD_DATA)['masterDataLocalizationData']['en']
machine = {}
for f in os.listdir(MACHINE):
    t = f[:-5]
    machine[t] = {r['id'] for r in load(os.path.join(MACHINE, f)) if isinstance(r, dict) and 'id' in r}

def norm(s): return re.sub(r'\s+', ' ', (s or '').replace('<br>', ' ').replace('\\n', ' ')).strip()

os.makedirs(os.path.join(HERE, 'report'), exist_ok=True)
summary = []
for table in sorted(mod):
    jp_path = os.path.join(HERE, 'jp', table + '.json')
    if not os.path.exists(jp_path) or not isinstance(mod[table], list): continue
    jp = {r['id']: r for r in load(jp_path) if isinstance(r, dict) and 'id' in r}
    rows = []
    stats = collections.Counter()
    for r in mod[table]:
        rid = r.get('id')
        src = jp.get(rid)
        if src is None: stats['missing-in-game'] += 1; continue
        for field, en in r.items():
            if field == 'id' or not isinstance(en, str) or not isinstance(src.get(field), str): continue
            ja = src[field].strip()
            if not ja: continue
            origin = 'machine' if rid in machine.get(table, ()) else 'official-import'
            off = official.get(ja)
            if off is None: verdict = 'no-reference'
            elif norm(off) == norm(en): verdict = 'matches-official'
            elif norm(off).lower() == norm(en).lower(): verdict = 'case-only'
            else: verdict = 'differs'
            stats[(origin, verdict)] += 1
            rows.append((rid, field, origin, verdict, ja, en, off or ''))
    if not rows: continue
    with open(os.path.join(HERE, 'report', table + '.tsv'), 'w', encoding='utf8') as w:
        w.write('id\tfield\torigin\tverdict\tjapanese\tmod_english\tofficial_english\n')
        for row in rows: w.write('\t'.join(str(x).replace('\t', ' ').replace('\n', '\\n') for x in row) + '\n')
    summary.append((table, len(rows), stats))

with open(os.path.join(HERE, 'report', 'summary.txt'), 'w', encoding='utf8') as w:
    w.write(f'{len(official)} official ja->en pairs from Barrel Wisdom\n\n')
    for table, n, st in sorted(summary, key=lambda x: -x[1]):
        parts = ', '.join(f'{k[0]}/{k[1]}={c}' if isinstance(k, tuple) else f'{k}={c}'
                          for k, c in sorted(st.items(), key=lambda kv: str(kv[0])))
        w.write(f'{table:32s} {n:6d} strings  {parts}\n')
print(open(os.path.join(HERE, 'report', 'summary.txt'), encoding='utf8').read())

"""Pair todo/<batch>.ja.txt with todo/<batch>.en.txt line by line into translations/<batch>.tsv."""
import os, re
HERE = os.path.dirname(os.path.abspath(__file__))
PH = re.compile(r'\{[^}]*\}|<[^>]+>|\n')
os.makedirs(os.path.join(HERE, 'translations'), exist_ok=True)
for f in sorted(os.listdir(os.path.join(HERE, 'todo'))):
    if not f.endswith('.en.txt'): continue
    b = f[:-7]
    ja = open(os.path.join(HERE, 'todo', b + '.ja.txt'), encoding='utf8').read().rstrip('\n').split('\n')
    en = open(os.path.join(HERE, 'todo', f), encoding='utf8').read().rstrip('\n').split('\n')
    if len(ja) != len(en): raise SystemExit(f'{b}: {len(ja)} Japanese lines but {len(en)} English lines')
    bad = [i + 1 for i, (a, e) in enumerate(zip(ja, en)) if sorted(PH.findall(a)) != sorted(PH.findall(e))]
    if bad: print(f'{b}: placeholder/line-break mismatch on lines {bad}')
    open(os.path.join(HERE, 'translations', b + '.tsv'), 'w', encoding='utf8').write(''.join(f'{a}\t{e}\n' for a, e in zip(ja, en)))
    print(f'{b}: {len(en)} merged')

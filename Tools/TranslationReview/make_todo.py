"""Split the still-Japanese strings into numbered batches under todo/ (one Japanese
string per line, "\n" escaped). Hand translations go in todo/<batch>.en.txt with the
same line numbers; merge_todo.py turns them into translations/<batch>.tsv."""
import os, collections
HERE = os.path.dirname(os.path.abspath(__file__))
rows = [l.split('\t') for l in open(os.path.join(HERE, 'report', 'untranslated.tsv'), encoding='utf8').read().split('\n')[1:] if l]
groups = collections.OrderedDict()
order = lambda t, f: 0 if (t, f) not in {('skill', 'description'), ('effect', 'description'), ('ability', 'description'), ('ability', 'name'), ('skill', 'summary')} else 1
seen = set()
for kind, table, rid, field, ja, cur in sorted(rows, key=lambda r: (order(r[1], r[3]), r[1], r[3])):
    if ja in seen: continue
    seen.add(ja)
    batch = 'a-visible' if order(table, field) == 0 else f'{table}-{field}'
    groups.setdefault(batch, []).append(ja)
os.makedirs(os.path.join(HERE, 'todo'), exist_ok=True)
for batch, items in groups.items():
    for part in range(0, len(items), 250):
        name = batch if len(items) <= 250 else f'{batch}-{part // 250 + 1:02d}'
        if os.path.exists(os.path.join(HERE, 'todo', name + '.en.txt')):
            print(name, 'already has English; left alone'); continue
        open(os.path.join(HERE, 'todo', name + '.ja.txt'), 'w', encoding='utf8').write('\n'.join(items[part:part + 250]) + '\n')
        print(name, len(items[part:part + 250]))

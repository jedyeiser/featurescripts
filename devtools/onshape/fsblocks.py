"""Split a FeatureScript file into top-level blocks.

Each block runs from the line after the previous block to the end of its definition, so it
carries its doc comment and any section divider above it. Relies on this codebase's style:
function / enum / predicate bodies close with '}' in column 0; a precondition group is
followed by a '{' in column 0; a const ends at the first ';' with balanced brackets.
"""
import re
import sys

START = re.compile(r'^(export\s+)?(const|function|enum|type|predicate)\s+([A-Za-z_][A-Za-z0-9_]*)')
STRING = re.compile(r'"(?:\\.|[^"\\])*"')


def balance(line):
    code = STRING.sub('""', line).split('//')[0]
    return sum(code.count(c) for c in '{[(') - sum(code.count(c) for c in '}])')


def blocks(lines):
    out = []
    i = 0
    prev_end = -1
    n = len(lines)
    while i < n:
        m = START.match(lines[i])
        if not m:
            i += 1
            continue
        kind, name = m.group(2), m.group(3)
        j = i
        if kind in ('function', 'enum', 'predicate'):
            while True:
                while j < n and lines[j].rstrip() != '}':
                    j += 1
                k = j + 1
                while k < n and lines[k].strip() == '':
                    k += 1
                if k < n and lines[k].rstrip() == '{':
                    j = k
                    continue
                break
        else:
            depth = 0
            while j < n:
                depth += balance(lines[j])
                if depth <= 0 and STRING.sub('""', lines[j]).split('//')[0].rstrip().endswith(';'):
                    break
                j += 1
        out.append({'name': name, 'kind': kind, 'exported': bool(m.group(1)), 'start': prev_end + 1, 'def': i, 'end': j})
        prev_end = j
        i = j + 1
    return out


if __name__ == '__main__':
    L = open(sys.argv[1], encoding='ascii').read().split('\n')
    for b in blocks(L):
        print('%5d %5d-%5d %-9s %s%s' % (b['start'] + 1, b['def'] + 1, b['end'] + 1, b['kind'], '' if b['exported'] else '(private) ', b['name']))

# Translates the browser game's TypeScript data tables (missions, ground school, qualifications) into C# object
# initialisers, driven by a schema of the C# classes. Run with any Python 3 (Blender's works):
#   blender -b --factory-startup --python Tools/ts2cs.py -- <ts file> <export name> <root class> <helper prefix> <out file>
# Only data literals are handled: objects, arrays, strings, template strings, numbers, booleans, identifiers,
# helper calls, spreads and simple arithmetic.
import re
import sys

# ------------------------------------------------------------------------------------------- schema
S, N, I, B = 'str', 'num', 'int', 'bool'
SA = 'str[]'

def E(name): return ('enum', name)
def EA(name): return ('enumarr', name)
def O(name): return ('obj', name)
def OA(name): return ('objarr', name)

SCHEMA = {
    'MissionDef': {
        'id': S, 'title': S, 'shortTitle': S, 'category': E('MissionCategory'), 'employer': S, 'aircraftId': S, 'summary': S,
        'briefing': SA, 'risks': SA, 'weatherId': S, 'weatherTimeline': OA('WeatherChange'), 'timeOfDay': E('TimeOfDay'),
        'timeOptions': EA('TimeOfDay'), 'weatherOptions': SA, 'startSiteId': S, 'baseSiteId': S, 'startHeadingDeg': N,
        'defaultFuelFrac': N, 'minFuelKg': N, 'payloadKg': N, 'manifest': OA('ManifestLeg'), 'dual': B,
        'objectives': ('objectives', 'ObjectiveDef'), 'assessment': O('AssessmentCriteria'), 'prerequisites': O('Prerequisites'),
        'estMinutes': N, 'rewards': ('rewards', None), 'availableFromMilestone': I, 'maxRangeM': N, 'remedialFor': EA('ScoreCategory'),
        'remedial': SA, 'pathway': E('Pathway'), 'searchAreas': OA('SearchAreaDef'), 'targets': OA('TargetDef'), 'casualty': O('Casualty'),
        'callsign': S, 'employerId': S, 'pay': N, 'minStanding': N, 'passengers': I,
    },
    'WeatherChange': {'min': N, 'id': S, 'report': S},
    'ManifestLeg': {'leg': S, 'pax': I, 'kg': N},
    'Casualty': {'name': S, 'condition': S, 'goldenMin': N},
    'SearchAreaDef': {'id': S, 'name': S, 'cx': N, 'cz': N, 'w': N, 'h': N, 'rotDeg': N},
    'TargetDef': {'id': S, 'name': S, 'kind': E('TargetKind'), 'x': N, 'z': N, 'areaId': S, 'onWater': B, 'conspicuity': N},
    'AssessmentCriteria': {'passMark': N, 'minimums': ('dict', 'ScoreCategory'), 'noExceedances': B, 'maxHardLandings': I},
    'Prerequisites': {'exercises': SA, 'quals': SA, 'theory': SA, 'exams': SA, 'minHours': N},
    'ObjectiveDef': {
        'kind': E('ObjectiveKind'), 'label': S, 'phase': S, 'say': S, 'deadlineMin': N, 'hood': B, 'job': S, 'rpm': N, 'minAglM': N,
        'siteId': S, 'radiusM': N, 'maxAglM': N, 'seconds': N, 'headingTolDeg': N, 'aboveFt': N, 'x': N, 'z': N, 'name': S,
        'altMinFt': N, 'altMaxFt': N, 'nearTarget': S, 'maxDistM': N, 'maxSlopeDeg': N, 'deliver': B, 'untilAglFt': N, 'rpmMin': N,
        'rpmMax': N, 'minIasKt': N, 'minAglFt': N, 'call': S, 'reply': S, 'from': S, 'prompt': S, 'options': OA('DecisionOption'),
        'payloadKg': N, 'what': S, 'pickup': B, 'areaId': S, 'targetId': S, 'hintAfterMin': N, 'system': E('SystemFailure'),
        'altFt': N, 'altTolFt': N, 'headingDeg': N, 'hdgTolDeg': N, 'mode': E('APMode'),
    },
    'DecisionOption': {'id': S, 'label': S, 'good': B, 'effect': E('DecisionEffect'), 'siteId': S, 'reply': S},
    # ground school
    'TheoryModule': {'id': S, 'title': S, 'minutes': N, 'sections': OA('TheorySection'), 'quiz': OA('Question'), 'passMark': N},
    'TheorySection': {'heading': S, 'body': S},
    'Question': {'id': S, 'q': S, 'options': SA, 'answer': I, 'explain': S},
    'ExamDef': {'id': S, 'title': S, 'modules': SA, 'questionCount': I, 'passMark': N, 'prerequisites': SA, 'description': S},
    'QualificationDef': {
        'id': S, 'name': S, 'abbrev': S, 'category': E('QualCategory'), 'description': S, 'prerequisites': SA, 'requiredHours': N,
        'requiredExercises': SA, 'theoryExams': SA, 'practicalTest': S, 'passCriteria': S, 'recurrent': S, 'restrictions': S,
        'unlocks': S, 'issuedBy': S, 'contentMilestone': I, 'requiresSenior': B,
    },
}
# C# field names that differ from the TypeScript ones (C# keywords)
RENAME = {'from': '@from', 'base': '@base'}

# ---------------------------------------------------------------------------------------- tokenizer
TOKEN = re.compile(r"""
    (?P<ws>\s+|//[^\n]*|/\*.*?\*/)
  | (?P<str>'(?:\\.|[^'\\])*'|"(?:\\.|[^"\\])*")
  | (?P<tpl>`(?:\\.|[^`\\])*`)
  | (?P<num>\d+(?:\.\d+)?(?:e[+-]?\d+)?)
  | (?P<spread>\.\.\.)
  | (?P<id>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)
  | (?P<op>\*\*|[{}\[\](),:?+\-*/])
  | (?P<other>.)
""", re.S | re.X)

def tokenize(src):
    out, pos = [], 0
    while pos < len(src):
        m = TOKEN.match(src, pos)
        if not m: raise SyntaxError(f'cannot read at {pos}: {src[pos:pos+40]!r}')
        pos = m.end()
        kind = m.lastgroup
        if kind == 'ws': continue
        out.append((kind, m.group()))
    return out

# ------------------------------------------------------------------------------------------- parser
class P:
    def __init__(self, toks): self.t, self.i = toks, 0
    def peek(self, k=0): return self.t[self.i + k] if self.i + k < len(self.t) else (None, None)
    def take(self, val=None):
        tok = self.t[self.i]
        if val is not None and tok[1] != val: raise SyntaxError(f'expected {val}, got {tok[1]} near {self.t[self.i-5:self.i+5]}')
        self.i += 1
        return tok

    def value(self):
        left = self.unary()
        while self.peek()[1] in ('+', '-', '*', '/'):
            op = self.take()[1]
            left = ('bin', op, left, self.unary())
        return left

    def unary(self):
        k, v = self.peek()
        if v == '-': self.take(); return ('neg', self.unary())
        if v == '(': self.take(); e = self.value(); self.take(')'); return ('paren', e)
        if v == '{': return self.obj()
        if v == '[': return self.arr()
        if k == 'str': self.take(); return ('str', unquote(v))
        if k == 'tpl': self.take(); return ('tpl', template(v))
        if k == 'num': self.take(); return ('num', v)
        if k == 'spread': self.take(); return ('spread', self.value())
        if k == 'id':
            self.take()
            if v in ('true', 'false'): return ('bool', v)
            if v in ('undefined', 'null'): return ('null',)
            if self.peek()[1] == '(':
                self.take('(')
                args = []
                while self.peek()[1] != ')':
                    args.append(self.value())
                    if self.peek()[1] == ',': self.take()
                self.take(')')
                return ('call', v, args)
            return ('id', v)
        raise SyntaxError(f'unexpected {v} near {self.t[self.i-5:self.i+5]}')

    def obj(self):
        self.take('{')
        fields = []
        while self.peek()[1] != '}':
            if self.peek()[0] == 'spread':
                self.take(); fields.append(('...', self.value()))
            else:
                key = self.take()[1].strip("'\"")
                if self.peek()[1] in (',', '}'): fields.append((key, ('id', key)))   # shorthand
                else:
                    self.take(':'); fields.append((key, self.value()))
            if self.peek()[1] == ',': self.take()
        self.take('}')
        return ('obj', fields)

    def arr(self):
        self.take('[')
        items = []
        while self.peek()[1] != ']':
            items.append(self.value())
            if self.peek()[1] == ',': self.take()
        self.take(']')
        return ('arr', items)

def unquote(s):
    body = s[1:-1]
    return re.sub(r"\\(.)", lambda m: {'n': '\n', 't': '\t'}.get(m.group(1), m.group(1)), body)

def template(s):
    body, parts, pos = s[1:-1], [], 0
    while True:
        j = body.find('${', pos)
        if j < 0: parts.append(('text', body[pos:])); break
        parts.append(('text', body[pos:j]))
        depth, k = 1, j + 2
        while depth:
            if body[k] == '{': depth += 1
            elif body[k] == '}': depth -= 1
            k += 1
        parts.append(('expr', P(tokenize(body[j + 2:k - 1])).value()))
        pos = k
    return parts

# ------------------------------------------------------------------------------------------ emitter
def cstr(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n') + '"'

def enum_val(enum, v):
    if not v: return f'{enum}.None'
    return f'{enum}.{v[0].upper()}{v[1:]}'

FUNCS = {'Math.round': 'Math.Round', 'Math.max': 'Math.Max', 'Math.min': 'Math.Min', 'Math.floor': 'Math.Floor'}

class Emit:
    def __init__(self, prefix): self.prefix = prefix

    def call_name(self, name):
        if name in FUNCS: return FUNCS[name]
        return f'{self.prefix}{name[0].upper()}{name[1:]}'

    def expr(self, n):
        k = n[0]
        if k == 'num': return n[1]
        if k == 'str': return cstr(n[1])
        if k == 'bool': return n[1]
        if k == 'null': return 'null'
        if k == 'id': return n[1]
        if k == 'neg': return '-' + self.expr(n[1])
        if k == 'paren': return '(' + self.expr(n[1]) + ')'
        if k == 'bin': return f'{self.expr(n[2])} {n[1]} {self.expr(n[3])}'
        if k == 'call': return f'{self.call_name(n[1])}({", ".join(self.expr(a) for a in n[2])})'
        if k == 'tpl':
            out = ''
            for kind, p in n[1]:
                if kind == 'text': out += p.replace('\\', '\\\\').replace('"', '\\"').replace('{', '{{').replace('}', '}}').replace('\n', '\\n')
                else: out += '{' + self.expr(p) + '}'
            return '$"' + out + '"'
        raise ValueError(f'expression {n}')

    def value(self, t, n, ind):
        if n[0] in ('id', 'call', 'bin', 'paren', 'neg') and t not in ('objectives',): return self.expr(n)
        if n[0] == 'null': return 'null'
        if t in (S, N, I, B): return self.expr(n)
        if t == SA: return 'new[] { ' + ', '.join(self.expr(x) for x in n[1]) + ' }' if n[1] else 'new string[0]'
        kind = t[0]
        if kind == 'enum': return enum_val(t[1], n[1]) if n[0] == 'str' else self.expr(n)
        if kind == 'enumarr': return 'new[] { ' + ', '.join(enum_val(t[1], x[1]) for x in n[1]) + ' }'
        if kind == 'dict':
            return f'new Dictionary<{t[1]}, double> {{ ' + ', '.join(f'[{enum_val(t[1], k)}] = {self.expr(v)}' for k, v in n[1]) + ' }'
        if kind == 'obj': return self.obj(t[1], n, ind)
        if kind == 'objarr':
            if not n[1]: return f'new {t[1]}[0]'
            inner = ind + '    '
            return 'new[]\n' + ind + '{\n' + ''.join(inner + self.item(t[1], x, inner) + ',\n' for x in n[1]) + ind + '}'
        if kind == 'objectives':
            inner = ind + '    '
            return 'Objs(\n' + ',\n'.join(inner + self.item('ObjectiveDef', x, inner) for x in n[1]) + ')'
        raise ValueError(f'type {t}')

    def item(self, cls, n, ind):
        if n[0] == 'spread': return self.expr(n[1])
        if n[0] in ('id', 'call'): return self.expr(n)
        return self.obj(cls, n, ind)

    def obj(self, cls, n, ind, multiline=False):
        schema = SCHEMA[cls]
        parts = []
        for key, v in n[1]:
            if key == '...': raise ValueError(f'spread inside {cls} object')
            if key not in schema: raise KeyError(f'{cls} has no field {key}')
            t = schema[key]
            if t[0] == 'rewards':
                xp = dict(v[1])['xp']
                parts.append(f'xp = {self.expr(xp)}')
                continue
            parts.append(f'{RENAME.get(key, key)} = {self.value(t, v, ind + "    ")}')
        if multiline:
            inner = ind + '    '
            return f'new {cls}\n{ind}{{\n' + ''.join(f'{inner}{p},\n' for p in parts) + f'{ind}}}'
        return f'new {cls} {{ ' + ', '.join(parts) + ' }'

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    src_path, export, cls, prefix, out_path = argv
    if prefix == '-': prefix = ''
    src = open(src_path, encoding='utf-8').read()
    m = re.search(r'export const ' + re.escape(export) + r'\s*:\s*[\w\[\]]+\s*=\s*', src)
    if not m: raise SystemExit(f'{export} not found')
    p = P(tokenize(src[m.end():]))
    arr = p.arr()
    em = Emit(prefix)
    out = []
    for item in arr[1]:
        out.append('            ' + em.obj(cls, item, '            ', multiline=True) + ',')
    open(out_path, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print(f'{export}: {len(arr[1])} entries written to {out_path}')

main()

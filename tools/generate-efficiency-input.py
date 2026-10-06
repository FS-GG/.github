#!/usr/bin/env python3
"""Generate closed efficiency input decoders; schema changes are explicit source changes."""
import argparse
import hashlib
import json
from pathlib import Path

FILES = [
    ('allocation-input-v1.schema.json', 'event', 'efficiency-resource-allocation/1'),
    ('episode-input-v1.schema.json', 'event', 'efficiency-problem-episode/1'),
    ('assessment-input-v1.schema.json', 'event', 'efficiency-assessment/1'),
    ('analysis-request-input-v1.schema.json', 'command', 'enqueue'),
    ('analysis-claim-input-v1.schema.json', 'command', 'claim'),
    ('analysis-attach-invocation-input-v1.schema.json', 'command', 'attach-invocation'),
    ('analysis-settle-input-v1.schema.json', 'command', 'settle'),
]
META = {'$schema', '$id', '$comment', 'description', 'title', '$defs'}
KEYS = {'type', 'additionalProperties', 'required', 'properties', 'items', 'maxItems',
        'minItems', 'minimum', 'maximum', 'minLength', 'maxLength', 'enum', 'const', 'pattern', 'format'}

HEADER = '''namespace FS.GG.Coord

open System
open System.Globalization
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// Generated closed shape validators. Producer authority, resource counters, reference
/// freshness and native eligibility are checked separately in the canonical store.
module EfficiencyInput =
    type Record = private Record of JsonElement

    let private fail label reason = raise (FormatException(label + ": " + reason))
    let private require label condition reason = if not condition then fail label reason
    let private integer label (node: JsonElement) =
        match node.TryGetInt64() with
        | true, value when node.ValueKind = JsonValueKind.Number -> value
        | _ -> fail label "expected int64"
    let private objectFields label allowed required (node: JsonElement) =
        require label (node.ValueKind = JsonValueKind.Object) "expected object"
        let names = node.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        require label (names.Length = (List.distinct names).Length) "duplicate property"
        require label (names |> List.forall (fun name -> List.contains name allowed)) "unknown property"
        require label (required |> List.forall (fun name -> List.contains name names)) "missing property"
    let private codePointLength (text: string) =
        let mutable runes = text.EnumerateRunes()
        let mutable count = 0
        while runes.MoveNext() do count <- count + 1
        count
    let private timestamp label (text: string) =
        require label (Regex.IsMatch(text, "^\\\\d{4}-\\\\d{2}-\\\\d{2}T\\\\d{2}:\\\\d{2}:\\\\d{2}(?:\\\\.\\\\d+)?(?:Z|[+-]\\\\d{2}:\\\\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)) "expected RFC3339 timestamp"
        match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, _ -> ()
        | _ -> fail label "invalid timestamp"

'''

def literal(text):
    return json.dumps(text, ensure_ascii=False)

def list_literal(values):
    return '[ ' + '; '.join(literal(value) for value in values) + ' ]'

def resolve(value, root):
    if '$ref' in value:
        if set(value) != {'$ref'} or not value['$ref'].startswith('#/$defs/'):
            raise ValueError('unsupported reference shape')
        return resolve(root['$defs'][value['$ref'].split('/')[-1]], root)
    unknown = set(value) - KEYS - META
    if unknown:
        raise ValueError(f'unsupported schema vocabulary: {sorted(unknown)}')
    result = {key: data for key, data in value.items() if key not in META}
    if 'properties' in result:
        result['properties'] = {key: resolve(data, root) for key, data in result['properties'].items()}
    if 'items' in result:
        result['items'] = resolve(result['items'], root)
    return result

class Emitter:
    def __init__(self):
        self.memo = {}
        self.functions = []

    def emit(self, shape):
        key = json.dumps(shape, sort_keys=True, separators=(',', ':'))
        if key in self.memo:
            return self.memo[key]
        kind = shape.get('type')
        nullable = isinstance(kind, list) and 'null' in kind
        if isinstance(kind, list):
            types = [item for item in kind if item != 'null']
            if len(types) != 1:
                raise ValueError('unsupported type union')
            kind = types[0]
        if kind is None:
            values = shape.get('enum', [shape.get('const')])
            if not all(isinstance(item, str) or item is None for item in values):
                raise ValueError('unsupported untyped enum')
            nullable = None in values
            kind = 'string'
        lines = []
        if kind == 'object':
            if shape.get('additionalProperties') is not False:
                raise ValueError('object must be closed')
            properties = shape['properties']
            required = shape.get('required', [])
            children = [(name, self.emit(child)) for name, child in properties.items()]
            lines.append(f'objectFields label {list_literal(properties)} {list_literal(required)} node')
            for name, function in children:
                access = f'{function} (label + {literal("." + name)}) (node.GetProperty {literal(name)})'
                if name in required:
                    lines.append(access)
                else:
                    lines += [f'match node.TryGetProperty {literal(name)} with',
                              f'| true, value -> {function} (label + {literal("." + name)}) value',
                              '| _ -> ()']
        elif kind == 'array':
            child = self.emit(shape['items'])
            lines.append('require label (node.ValueKind = JsonValueKind.Array) "expected array"')
            for bound, op in [('minItems', '>='), ('maxItems', '<=')]:
                if bound in shape:
                    lines.append(f'require label (node.GetArrayLength() {op} {shape[bound]}) "array bound exceeded"')
            lines.append(f'node.EnumerateArray() |> Seq.iteri (fun index child -> {child} (label + "[" + string index + "]") child)')
        elif kind == 'string':
            lines += ['require label (node.ValueKind = JsonValueKind.String) "expected string"', 'let text = node.GetString()']
            for bound, op in [('minLength', '>='), ('maxLength', '<=')]:
                if bound in shape:
                    lines.append(f'require label (codePointLength text {op} {shape[bound]}) "string bound exceeded"')
            if 'const' in shape:
                lines.append(f'require label (text = {literal(shape["const"])}) "unsupported version"')
            if 'enum' in shape:
                values = [value for value in shape['enum'] if value is not None]
                lines.append(f'require label (Set.contains text (set {list_literal(values)})) "unsupported value"')
            if 'pattern' in shape:
                lines.append(f'require label (Regex.IsMatch(text, {literal(shape["pattern"])}, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)) "invalid identifier or digest"')
            if shape.get('format') == 'date-time':
                lines.append('timestamp label text')
            elif 'format' in shape:
                raise ValueError('unsupported string format')
        elif kind == 'integer':
            lines.append('let number = integer label node')
            for bound, op in [('minimum', '>='), ('maximum', '<=')]:
                if bound in shape:
                    lines.append(f'require label (number {op} {shape[bound]}L) "integer bound exceeded"')
            if len(lines) == 1:
                lines.append('ignore number')
        elif kind == 'null':
            lines.append('require label (node.ValueKind = JsonValueKind.Null) "expected null"')
        elif kind == 'boolean':
            lines.append('require label (node.ValueKind = JsonValueKind.True || node.ValueKind = JsonValueKind.False) "expected boolean"')
            if 'const' in shape:
                lines.append(f'require label (node.GetBoolean() = {str(shape["const"]).lower()}) "invalid constant"')
        else:
            raise ValueError(f'unsupported type: {kind}')
        name = 'check' + str(len(self.functions))
        self.memo[key] = name
        prefix = f'    let private {name} (label: string) (node: JsonElement) =\n'
        if nullable:
            prefix += '        if node.ValueKind = JsonValueKind.Null then ()\n        else\n'
            indent = '            '
        else:
            indent = '        '
        self.functions.append(prefix + '\n'.join(indent+line for line in lines) + '\n\n')
        return name

def generate(schema_root):
    emitter = Emitter()
    roots, provenance, reference_shapes = [], [], []
    for filename, family, discriminator in FILES:
        raw = (schema_root / filename).read_bytes()
        root = json.loads(raw)
        shape = resolve(root, root)
        reference_shapes.append(resolve(root['$defs']['sourceRef'], root))
        if family == 'event':
            shape['properties'].pop('schema')
            shape['required'].remove('schema')
        roots.append((family, discriminator, emitter.emit(shape)))
        provenance.append(f'// {filename} sha256:{hashlib.sha256(raw).hexdigest()}')
    # Allocation's canonical reference includes its digest. Assessment semantic refs
    # intentionally have a different vocabulary and are not canonical authorities.
    reference_name = emitter.emit(reference_shapes[0])
    output = '\n'.join(provenance) + '\n' + HEADER + ''.join(emitter.functions)
    output += '''    let private validate check node =
        try check "efficiency" node; Ok()
        with
        | :? FormatException as error -> Error error.Message
        | :? InvalidOperationException as error -> Error error.Message
        | :? RegexMatchTimeoutException -> Error "efficiency pattern deadline"

    let parseEvent (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then Error "efficiency event must be object"
        else
            match node.TryGetProperty "kind" with
            | true, kind when kind.ValueKind = JsonValueKind.String ->
                let check =
                    match kind.GetString() with
'''
    for family, discriminator, function in roots:
        if family == 'event':
            output += f'                    | {literal(discriminator)} -> Some {function}\n'
    output += '''                    | _ -> None
                match check with
                | Some check -> validate check node |> Result.map (fun () -> Record(node.Clone()))
                | None -> Error "unsupported efficiency event"
            | _ -> Error "missing efficiency kind"

    let validateCommand action node =
        match action with
'''
    for family, discriminator, function in roots:
        if family == 'command':
            output += f'        | {literal(discriminator)} -> validate {function} node\n'
    return output + '''        | _ -> Error "unsupported efficiency action"

    let validateReference node = validate REFERENCE_CHECK node

    let body (Record node) = node
'''.replace('REFERENCE_CHECK', reference_name)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--schema-root', type=Path, default=Path('contracts/process-efficiency'))
    parser.add_argument('--output', type=Path, default=Path('src/FS.GG.Coord.Core/EfficiencyInput.fs'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    generated = generate(args.schema_root)
    if args.check:
        if args.output.read_text() != generated:
            raise SystemExit('efficiency input decoder differs from pinned schemas')
    else:
        args.output.write_text(generated)

if __name__ == '__main__':
    main()

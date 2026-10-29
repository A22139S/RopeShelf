"""Scan release-source candidates without printing matched secret values. Standard library only."""
from pathlib import Path
import argparse
import json
import re
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('root', nargs='?', default='.')
parser.add_argument('--tracked', action='store_true', help='Scan exactly git ls-files, including staged additions')
parser.add_argument('--forbid', action='append', default=[], help='Additional private string to reject; value is never printed')
args = parser.parse_args()
root = Path(args.root).resolve()
excluded = {'.git', 'bin', 'obj', 'artifacts', '__pycache__', '.vs', '.idea', '.vscode'}
if args.tracked:
    result = subprocess.run(['git', '-C', str(root), 'ls-files', '-z'], check=True, capture_output=True)
    files = [root / p.decode('utf-8') for p in result.stdout.split(b'\0') if p]
else:
    files = [p for p in root.rglob('*') if p.is_file() and not excluded.intersection(p.relative_to(root).parts)]
rules = {
    'github-token': r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b',
    'aws-key': r'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b',
    'service-api-key': r'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}\b',
    'slack-token': r'\bxox[baprs]-[A-Za-z0-9-]{20,}\b',
    'bearer-token': r'(?i)\bBearer\s+[A-Za-z0-9._-]{24,}',
    'private-key': r'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    'assigned-secret': r'''(?i)(?:password|passwd|api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]\s*['"][^'"\r\n]{8,}['"]''',
    'credential-in-url': r'https?://[^\s/:]+:[^\s/@]+@',
    'windows-user-path': r'(?i)\b[A-Z]:[\\/]Users[\\/][^\s"\r\n]+',
    'unix-user-path': r'/(?:home|Users)/[A-Za-z0-9_.-]+/',
    'personal-email': r'\b[A-Za-z0-9_.+-]+@(?:qq\.com|163\.com|126\.com|gmail\.com|outlook\.com)\b',
}
compiled = {name: re.compile(pattern) for name, pattern in rules.items()}
findings = []
for path in sorted(files):
    relative = path.relative_to(root).as_posix()
    if path.suffix.lower() in {'.pfx', '.p12', '.pem', '.key', '.pdb', '.exe', '.dll', '.nupkg', '.zip'} or path.name.startswith('.env'):
        findings.append({'file': relative, 'rule': 'sensitive-or-build-file'})
    data = path.read_bytes()
    for number, private in enumerate(args.forbid):
        if private.encode('utf-8') in data or private.encode('utf-16-le') in data:
            findings.append({'file': relative, 'rule': f'private-string-{number + 1}'})
    if path.suffix.lower() in {'.ico', '.png'}:
        continue
    try:
        value = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        findings.append({'file': relative, 'rule': 'unexpected-binary'})
        continue
    for name, regex in compiled.items():
        for match in regex.finditer(value):
            findings.append({'file': relative, 'line': value.count('\n', 0, match.start()) + 1, 'rule': name})
print(json.dumps({'filesScanned': len(files), 'findings': findings, 'passed': not findings,
                  'scope': 'tracked files' if args.tracked else 'source candidates',
                  'limitation': 'Pattern scan plus explicit private strings; not a guarantee that all secrets are detectable.'}, ensure_ascii=False, indent=2))
raise SystemExit(1 if findings else 0)

#!/usr/bin/env python3
"""Package source plus ModelGuard for five Revit releases; never ship host API DLLs."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib

root = Path(__file__).resolve().parent.parent
paths = []
for folder in ('src', 'tests', 'tools', 'scripts', 'build', 'docs', 'samples', 'examples', '.github'):
    paths.extend(p for p in (root / folder).rglob('*') if p.is_file() and not {'bin', 'obj', '__pycache__', 'node_modules'}.intersection(p.relative_to(root).parts))
paths.extend(root / name for name in ('README.md', '.gitignore', '.gitattributes', 'global.json', 'Directory.Build.props', 'BimPortfolio.sln'))
for year in range(2021, 2026):
    tfm = 'net8.0-windows' if year == 2025 else 'net48'
    for plugin in ('ModelGuard',):
        folder = root / 'artifacts' / str(year) / plugin / 'Release' / tfm
        for dependency in (plugin + '.dll', 'BimPortfolio.Core.dll', 'BimPortfolio.Revit.dll', 'BimPortfolio.UI.dll', 'Newtonsoft.Json.dll', 'BimPortfolio.Presentation.dll', 'ru/BimPortfolio.Presentation.resources.dll'):
            if not (folder / dependency).is_file():
                raise SystemExit(f'Missing {folder / dependency}; build all versions first.')
        paths.extend(p for p in folder.rglob('*') if p.is_file())
if any(p.name.lower() in {'revitapi.dll', 'revitapiui.dll'} for p in paths):
    raise SystemExit('Refusing to distribute host API DLLs.')
archive = root / 'artifacts' / 'model-guard-revit.zip'
archive.parent.mkdir(exist_ok=True)
with ZipFile(archive, 'w', ZIP_DEFLATED) as output:
    for path in sorted(set(paths)):
        output.write(path, path.relative_to(root).as_posix())
with ZipFile(archive) as output:
    if output.testzip() is not None:
        raise SystemExit('Archive integrity check failed.')
checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.zip.sha256').write_text(f'{checksum}  {archive.name}\n')
print(f'{archive}: {archive.stat().st_size:,} bytes, {len(set(paths))} files; integrity passed.')

<#
.SYNOPSIS
  Export a repos relevant source into ONE text file you can hand to an AI / reviewer.
  Skips venv, node_modules, lockfiles, build output, binaries, .env files, huge/minified
  files and previous exports. Redacts hardcoded secrets by default.

.USAGE
  cd F:\devops\Net_Predict
  powershell -ExecutionPolicy Bypass -File .\Export-Source.ps1

  Options:
    -Path       Repo root (default: current folder)
    -Out        Output file name/path (default: source_export.txt)
    -MaxFileKB  Skip any single file bigger than this (default: 150)
    -NoRedact   Do NOT redact secret-looking assignments (not recommended)

  Works on Windows PowerShell 5.1 and PowerShell 7. If the folder is a git repo
  (and git is installed) it uses `git ls-files`, so your .gitignore is respected.
#>
param(
  [string]$Path = ".",
  [string]$Out = "source_export.txt",
  [int]$MaxFileKB = 150,
  [switch]$NoRedact
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $Path).Path.TrimEnd('\', '/')
if ([IO.Path]::IsPathRooted($Out)) { $outPath = $Out } else { $outPath = Join-Path $root $Out }
$outPath = [IO.Path]::GetFullPath($outPath)

# ---------- rules (edit freely) ----------
$excludeDirs = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
@(
  'node_modules','bower_components','jspm_packages','vendor',
  'venv','.venv','env','.env.d','virtualenv','site-packages','__pycache__','.tox','.nox',
  '.mypy_cache','.pytest_cache','.ruff_cache','.ipynb_checkpoints','.eggs',
  '.git','.hg','.svn',
  'dist','build','out','bin','obj','target','coverage','htmlcov','.cache','.parcel-cache',
  '.next','.nuxt','.svelte-kit','.turbo','.angular','.expo','.dart_tool','.gradle','.terraform',
  '.idea','.vscode','.vs','Pods','DerivedData',
  'staticfiles','media','uploads','logs','tmp','temp'
) | ForEach-Object { [void]$excludeDirs.Add($_) }

$excludeFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
@(
  'package-lock.json','yarn.lock','pnpm-lock.yaml','bun.lockb','poetry.lock','Pipfile.lock',
  'composer.lock','Cargo.lock','go.sum','Gemfile.lock','uv.lock','npm-shrinkwrap.json',
  'Export-Source.ps1','.DS_Store','Thumbs.db'
) | ForEach-Object { [void]$excludeFiles.Add($_) }

$includeExt = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
@(
  '.py','.pyi','.js','.jsx','.mjs','.cjs','.ts','.tsx','.vue','.svelte','.astro',
  '.html','.htm','.css','.scss','.sass','.less',
  '.java','.kt','.kts','.go','.rs','.rb','.php','.cs','.c','.h','.cpp','.hpp','.swift','.dart','.scala',
  '.sql','.sh','.bash','.ps1','.bat','.cmd',
  '.json','.yml','.yaml','.toml','.ini','.cfg','.conf','.xml','.gradle','.proto','.graphql','.prisma','.tf',
  '.md','.rst','.dockerignore','.gitignore','.editorconfig','.env.example','.example'
) | ForEach-Object { [void]$includeExt.Add($_) }

# Extensionless / special filenames that are always worth including
$includeNames = @('Dockerfile','Makefile','Procfile','Jenkinsfile','Vagrantfile','Gemfile','Pipfile','.env.example')

# Secret-looking assignments get their value redacted (unless it just reads an env var)
$secretPattern = '(?im)^([ \t]*[\w\.\-"'']*(?:password|passwd|secret|token|api[_-]?key|passkey|private[_-]?key|consumer[_-]?key|client[_-]?secret)[\w\.\-"'']*[ \t]*[:=][ \t]*)([^\r\n]+)$'
$envReader     = 'getenv|environ|process\.env|import\.meta\.env|\benv\(|\bconfig\(|settings\.|os\.'

# ---------- gather candidate files ----------
$skipped = @{}
function Skip([string]$why) { if ($skipped.ContainsKey($why)) { $skipped[$why]++ } else { $skipped[$why] = 1 } }

function Walk([string]$dir) {
  foreach ($f in [IO.Directory]::GetFiles($dir)) { $f }
  foreach ($d in [IO.Directory]::GetDirectories($dir)) {
    $name = [IO.Path]::GetFileName($d)
    if ($excludeDirs.Contains($name) -or $name -like '*.egg-info') { Skip 'excluded folder'; continue }
    if (([IO.File]::GetAttributes($d) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
    Walk $d
  }
}

$mode = 'folder walk'
$candidates = $null
if ((Test-Path (Join-Path $root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
  try {
    $lines = & git -C $root -c core.quotepath=off ls-files --cached --others --exclude-standard 2>$null
    if ($LASTEXITCODE -eq 0 -and $lines) {
      $candidates = $lines | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
      $mode = 'git ls-files (.gitignore respected)'
    }
  } catch { $candidates = $null }
}
if (-not $candidates) { $candidates = Walk $root }

# ---------- filter + read ----------
$included = New-Object System.Collections.Generic.List[object]
$maxBytes = $MaxFileKB * 1024
$redactCount = 0

foreach ($full in $candidates) {
  $full = [IO.Path]::GetFullPath($full)
  if ($full -eq $outPath) { continue }
  $rel  = $full.Substring($root.Length).TrimStart('\', '/')
  $name = [IO.Path]::GetFileName($full)
  $ext  = [IO.Path]::GetExtension($full)

  # any folder segment excluded? (needed for git mode)
  $segs = $rel -split '[\\/]'
  $badDir = $false
  for ($i = 0; $i -lt $segs.Length - 1; $i++) { if ($excludeDirs.Contains($segs[$i]) -or $segs[$i] -like '*.egg-info') { $badDir = $true; break } }
  if ($badDir) { Skip 'excluded folder'; continue }

  if ($excludeFiles.Contains($name))                                   { Skip 'lockfile / ignored name'; continue }
  if ($name -match '^\.env(\..+)?$' -and $name -notmatch '\.example$') { Skip '.env (secrets)'; continue }
  if ($name -match '\.min\.(js|css)$' -or $name -match '\.map$')       { Skip 'minified / sourcemap'; continue }

  $isWanted = $includeExt.Contains($ext) -or ($includeNames -contains $name) -or
              ($name -like '*.Dockerfile') -or ($name -like 'Dockerfile.*') -or
              ($name -like 'requirements*.txt')
  if (-not $isWanted) { Skip 'not a source extension'; continue }

  $info = New-Object IO.FileInfo $full
  if ($info.Length -gt $maxBytes) { Skip "larger than $MaxFileKB KB"; continue }
  if ($info.Length -eq 0)         { Skip 'empty file'; continue }

  # binary check (NUL byte in first 8 KB)
  $fs = [IO.File]::OpenRead($full)
  try { $buf = New-Object byte[] ([Math]::Min(8192, $info.Length)); [void]$fs.Read($buf, 0, $buf.Length) } finally { $fs.Dispose() }
  if ($buf -contains 0) { Skip 'binary'; continue }

  $text = [IO.File]::ReadAllText($full)
  if (-not $NoRedact) {
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
      param($m)
      if ($m.Groups[2].Value -match $envReader) { return $m.Value }
      $script:redactCount++
      return $m.Groups[1].Value + '"<REDACTED>"'
    }
    $text = [regex]::Replace($text, $secretPattern, $evaluator)
  }

  $included.Add([pscustomobject]@{
    Rel   = $rel.Replace('\', '/')
    Text  = $text
    Lines = ($text -split "`n").Count
    Bytes = $info.Length
  })
}

$included = $included | Sort-Object Rel

# ---------- write output ----------
$utf8 = New-Object System.Text.UTF8Encoding($false)
$w = New-Object IO.StreamWriter($outPath, $false, $utf8)
try {
  $w.WriteLine("==================== PROJECT: $(Split-Path $root -Leaf) ====================")
  $w.WriteLine("Exported: $(Get-Date -Format 'yyyy-MM-dd HH:mm')   Mode: $mode   Files: $($included.Count)")
  $w.WriteLine()
  $w.WriteLine("==================== FILE TREE (path - lines) ====================")
  foreach ($f in $included) { $w.WriteLine("$($f.Rel)  ($($f.Lines))") }
  $w.WriteLine()
  $w.WriteLine("==================== SOURCE CODE ====================")
  foreach ($f in $included) {
    $w.WriteLine()
    $w.WriteLine("FILE_PATH: $($f.Rel)")
    $w.WriteLine(('-' * 79))
    $w.WriteLine($f.Text.TrimEnd())
  }
} finally { $w.Dispose() }

# ---------- report ----------
$totalLines = ($included | Measure-Object Lines -Sum).Sum
$sizeKB = [Math]::Round((Get-Item $outPath).Length / 1KB)
Write-Host ""
Write-Host "Done -> $outPath" -ForegroundColor Green
Write-Host "  Files: $($included.Count)   Lines: $totalLines   Size: $sizeKB KB   Secrets redacted: $redactCount   Mode: $mode"
Write-Host "  Skipped:"
$skipped.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Host ("    {0,6}  {1}" -f $_.Value, $_.Key) }
Write-Host "  Biggest included files (check nothing junky slipped in):"
$included | Sort-Object Lines -Descending | Select-Object -First 5 | ForEach-Object { Write-Host ("    {0,6} lines  {1}" -f $_.Lines, $_.Rel) }
if ($sizeKB -gt 3000) { Write-Host "  WARNING: export is over 3 MB - add noisy folders to `$excludeDirs." -ForegroundColor Yellow }

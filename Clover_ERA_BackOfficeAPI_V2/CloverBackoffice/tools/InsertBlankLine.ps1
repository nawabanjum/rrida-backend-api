Continue='Stop'
 = Get-ChildItem -Path . -Recurse -Include *.cs -File | Where-Object { .FullName -notmatch '\\(bin|obj)\\' }
 = '(?ms)(^(?:\s*\[[^\]]+\]\s*\r?\n)+)\s*(?=(\s*(?:public|private|protected|internal)\s+(?:async\s+)?(?:partial\s+)?(?:static\s+)?[^\r\n]*\())'
 = [System.Text.RegularExpressions.Regex]::new(, [System.Text.RegularExpressions.RegexOptions]::Multiline -bor [System.Text.RegularExpressions.RegexOptions]::Singleline)
foreach ( in ) {
   = Get-Content -LiteralPath .FullName -Raw
   = .Replace(, { param() .Groups[1].Value +  
 })
  if ( -ne ) {
    [System.IO.File]::WriteAllText(.FullName, , [System.Text.UTF8Encoding]::new(False))
    Write-Output Updated: 
  }
}

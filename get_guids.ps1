# Run this from C:\Users\pkerley\Documents\Kentico (same place you ran --kxp-ci-store)
# Pulls the 3 GUIDs needed for the UMT import: website channel, content language, StandardContentPage content type.

$repo = "App_Data\CIRepository\@global"

Write-Host "`n--- Website Channel GUID ---"
Get-ChildItem "$repo\cms.websitechannel" -Recurse -Filter *.xml | ForEach-Object {
    Write-Host "$($_.Directory.Name): $($_.BaseName)"
}

Write-Host "`n--- Content Language GUID(s) ---"
Get-ChildItem "$repo\cms.contentlanguage" -Filter *.xml | ForEach-Object {
    [xml]$xml = Get-Content $_.FullName
    $guid = $xml.SelectSingleNode("//ContentLanguageGUID")."#text"
    $name = $xml.SelectSingleNode("//ContentLanguageDisplayName")."#text"
    Write-Host "$($_.BaseName) ($name): $guid"
}

Write-Host "`n--- StandardContentPage Content Type GUID ---"
$ctFile = Get-ChildItem "$repo\cms.contenttype" -Filter "*standardcontentpage*.xml" -Recurse
if ($ctFile) {
    [xml]$xml = Get-Content $ctFile.FullName
    $guid = $xml.SelectSingleNode("//ClassGUID")."#text"
    Write-Host "$($ctFile.Name): $guid"
} else {
    Write-Host "Could not find a file matching *standardcontentpage*.xml under $repo\cms.contenttype -- list that folder manually:"
    Get-ChildItem "$repo\cms.contenttype" -Filter *.xml | Select-Object Name
}



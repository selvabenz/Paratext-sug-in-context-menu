[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$UserName)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testDir = Join-Path $projectRoot 'artifacts\disposable-project\NSTest'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$settings = New-Object Xml.XmlDocument
$settings.LoadXml('<ScriptureText><Name>NSTest</Name><FullName>Note Suggestions Disposable Test</FullName><Encoding>65001</Encoding><Language>Tamil</Language><LanguageIsoCode>ta:::</LanguageIsoCode><DefaultFont>Meera Inimai</DefaultFont><DefaultFontSize>12</DefaultFontSize><TranslationInfo>Standard::</TranslationInfo><Editable>T</Editable><StyleSheet>usfm.sty</StyleSheet><FileNamePrePart/><FileNameBookNameForm>41MAT</FileNameBookNameForm><FileNamePostPart>NSTest.SFM</FileNamePostPart><Versification>4</Versification><Guid/><BooksPresent/></ScriptureText>')
$settings.ScriptureText.Guid = [Guid]::NewGuid().ToString('N')
$settings.ScriptureText.BooksPresent = '1' + ('0' * 122)
$settings.Save((Join-Path $testDir 'Settings.xml'))
$scripture = @'
\id GEN NSTest
\h Note Suggestions Test
\toc1 Note Suggestions Test
\toc2 Test
\toc3 GEN
\mt1 Synthetic test only
\c 1
\p
\v 1 இது பழைய சொல்.
\v 2 பழைய சொல் மற்றும் பழைய சொல்.
\v 3 உடல் உரை\f + \fr 1:3 \ft பழைய குறிப்பு.\f* முடிவு.
'@
[IO.File]::WriteAllText((Join-Path $testDir '01GENNSTest.SFM'), $scripture + "`r`n", $utf8)
$notes = New-Object Xml.XmlDocument
$notes.LoadXml('<CommentList/>')
$cases = @(
    @{ Verse = 1; Topic = 'Punctuation'; Category = 'Punctuation'; Original = 'பழைய சொல்'; Suggested = 'புதிய சொல்'; Decision = 'No' },
    @{ Verse = 2; Topic = 'Grammar'; Category = 'Grammar'; Original = 'பழைய சொல்'; Suggested = 'புதிய சொல்'; Decision = 'No' },
    @{ Verse = 3; Topic = 'Footnote'; Category = 'Footnote / cross-reference'; Original = 'பழைய குறிப்பு.'; Suggested = 'புதிய குறிப்பு.'; Decision = 'Yes' }
)
foreach ($case in $cases) {
    $comment = $notes.CreateElement('Comment')
    foreach ($attr in @{ Thread = [Guid]::NewGuid().ToString('N').Substring(0,8); User = $UserName; VerseRef = ('GEN 1:' + $case.Verse); Language = 'ta'; Date = [DateTimeOffset]::Now.ToString('O') }.GetEnumerator()) { $comment.SetAttribute($attr.Key,$attr.Value) }
    foreach ($field in @{ SelectedText = $case.Original; StartPosition = '0'; ContextBefore = ''; ContextAfter = ''; Status = 'todo'; Type = ''; ConflictType = 'unknownConflictType'; HideInTextWindow = 'false' }.GetEnumerator()) { $element = $notes.CreateElement($field.Key); $element.InnerText = $field.Value; [void]$comment.AppendChild($element) }
    $contents = $notes.CreateElement('Contents')
    foreach ($line in @("[AI-QA R2] $($case.Topic) — $($case.Category)", "Original: $($case.Original)", "Suggested: $($case.Suggested)", 'Reason: Synthetic plugin acceptance fixture.', "Severity: Low · Confidence: High · Reviewer decision needed: $($case.Decision)", 'AI proposal — human review required. Not an edit.')) { $paragraph = $notes.CreateElement('p'); $paragraph.InnerText = $line; [void]$contents.AppendChild($paragraph) }
    [void]$comment.AppendChild($contents); [void]$notes.DocumentElement.AppendChild($comment)
}
$notes.Save((Join-Path $testDir ("Notes_" + $UserName + '.xml')))
$access = New-Object Xml.XmlDocument
$access.LoadXml('<ProjectUserAccess PeerSharing="false"><User FirstUser="true" UnregisteredUser="false"><Role>Administrator</Role><AllBooks>true</AllBooks><Books/><Permissions/></User></ProjectUserAccess>')
$access.ProjectUserAccess.User.SetAttribute('UserName',$UserName)
$access.Save((Join-Path $testDir 'ProjectUserAccess.xml'))
[IO.File]::WriteAllText((Join-Path $testDir 'CommentTags.xml'), '<TagList><Tag Id="1" Name="To Do" Icon="01flag1" CreatorResolve="false"/><LastUsedID>1</LastUsedID></TagList>', $utf8)
Write-Output "Prepared synthetic project at $testDir. Copy NSTest to the Paratext projects directory while Paratext is closed; do not overwrite an existing project."

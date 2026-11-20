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
\v 4 அவன் வீட்டிற்குப் போனான்.\f + \fr 1:4 \ft சில பிரதிகளில் வீட்டிற்கு என்று உள்ளது.\f*
\v 5 இந்த வசனம் சில பிரதிகளில் இல்லை.
\v 6 அவர்கள் வந்தான் மற்றும் பேசினான்.
\q1
\v 7 கர்த்தர் என் மேய்ப்பர்.
\p
\v 8 அவன் பழைய வழியில் நடந்தான்.
\v 9 இதினால் அவன் அறிந்தான்.
\v 10 அவன் நகரத்திற்கு\f + \fr 1:10 \ft பழைய விளக்கம்\f* வந்தான்.
\s பழைய தலைப்பு
\p
\v 11 புதிய பகுதி தொடங்குகிறது.
\v 12 \x + \xo 1:12 \xt ஆதி 12:7; ஆதி 26:3\x*அவன் சொன்னான்.
\v 13 அவன் அங்கே இருந்தான்.\x + \xo 1:13 \xt யாத் 3:1; யாத் 4:2\x*
'@
[IO.File]::WriteAllText((Join-Path $testDir '01GENNSTest.SFM'), $scripture + "`r`n", $utf8)
$notes = New-Object Xml.XmlDocument
$notes.LoadXml('<CommentList/>')
$cases = @(
    @{ Verse = 1; Topic = 'Punctuation'; Category = 'Punctuation'; Original = 'பழைய சொல்'; Suggested = 'புதிய சொல்'; Decision = 'No' },
    @{ Verse = 2; Topic = 'Grammar'; Category = 'Grammar'; Original = 'பழைய சொல்'; Suggested = 'புதிய சொல்'; Decision = 'No' },
    @{ Verse = 3; Topic = 'Footnote'; Category = 'Footnote / cross-reference'; Original = 'பழைய குறிப்பு.'; Suggested = 'புதிய குறிப்பு.'; Decision = 'Yes' },
    # Fast review cases: no ready-to-paste Tamil, an existing variant footnote, two notes in one verse, poetry.
    @{ Verse = 4; Topic = 'Source reading'; Category = 'Source comparison'; Original = 'வீட்டிற்குப்'; Suggested = 'Use the BSI reading here'; Decision = 'Yes' },
    @{ Verse = 5; Topic = 'Verse omission'; Category = 'Textual-basis review'; Original = 'இந்த வசனம்'; Suggested = 'Add a footnote: சில பிரதிகளில் இந்த வசனம் காணப்படவில்லை.'; Decision = 'Yes' },
    @{ Verse = 6; Topic = 'Plural'; Category = 'Plural agreement'; Original = 'வந்தான்'; Suggested = 'வந்தார்கள்'; Decision = 'No' },
    @{ Verse = 6; Topic = 'Grammar'; Category = 'Grammar'; Original = 'பேசினான்'; Suggested = 'பேசினார்கள்'; Decision = 'No' },
    @{ Verse = 7; Topic = 'Case marker'; Category = 'Case/object marker'; Original = 'என் மேய்ப்பர்'; Suggested = 'என்னுடைய மேய்ப்பர்'; Decision = 'No' },
    # Wording read from the note: an English remark, alternatives, words quoted from a heading the note does not name.
    @{ Verse = 8; Topic = 'Source reading'; Category = 'Source comparison'; Original = 'பழைய வழியில்'; Suggested = 'புதிய வழியில் (for reviewer)'; Decision = 'Yes' },
    @{ Verse = 9; Topic = 'Spelling'; Category = 'Name consistency'; Original = 'இதினால்'; Suggested = 'இதனால் / அதினால்'; Decision = 'Yes' },
    @{ Verse = 11; Topic = 'Spelling'; Category = 'Name consistency'; Original = 'பழைய தலைப்பு'; Suggested = 'புதிய தலைப்பு'; Decision = 'Yes' },
    # Marker changes: footnote text, cross-reference text, and a Suggested line that gives only the changed part (must not be armed).
    @{ Verse = 10; Topic = 'Footnote text'; Category = 'Footnote / cross-reference'; Original = '\ft பழைய விளக்கம்\f*'; Suggested = '\ft புதிய விளக்கம்\f*'; Decision = 'Yes'; Selected = '' },
    @{ Verse = 12; Topic = 'Cross-reference'; Category = 'Footnote / cross-reference'; Original = '\xt ஆதி 12:7; ஆதி 26:3\x*'; Suggested = '\xt ஆதி 12:7; 26:3\x*'; Decision = 'Yes'; Selected = '' },
    @{ Verse = 13; Topic = 'Cross-reference'; Category = 'Footnote / cross-reference'; Original = 'இருந்தான்.\x + \xo 1:13 \xt யாத் 3:1; யாத் 4:2\x*'; Suggested = 'யாத் 3:1; 4:2'; Decision = 'Yes'; Selected = '' }
)
foreach ($case in $cases) {
    $comment = $notes.CreateElement('Comment')
    foreach ($attr in @{ Thread = [Guid]::NewGuid().ToString('N').Substring(0,8); User = $UserName; VerseRef = ('GEN 1:' + $case.Verse); Language = 'ta'; Date = [DateTimeOffset]::Now.ToString('O') }.GetEnumerator()) { $comment.SetAttribute($attr.Key,$attr.Value) }
    $selected = if ($case.ContainsKey('Selected')) { $case.Selected } else { $case.Original }
    foreach ($field in @{ SelectedText = $selected; StartPosition = '0'; ContextBefore = ''; ContextAfter = ''; Status = 'todo'; Type = ''; ConflictType = 'unknownConflictType'; HideInTextWindow = 'false' }.GetEnumerator()) { $element = $notes.CreateElement($field.Key); $element.InnerText = $field.Value; [void]$comment.AppendChild($element) }
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

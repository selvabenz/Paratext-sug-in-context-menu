# Paratext sug in context menu

Show suggestions from existing Paratext project notes in the Scripture editor's right-click context menu.

Target: **Paratext 9.5.110.1 on Windows**.

## Status

Planning repository. No plugin has been built, installed, or tested in Paratext yet.

The user's screenshot shows QA project notes with `Original:`, `Suggested:`, and `Reason:` fields. The menu should surface the existing `Suggested:` correction for the relevant passage. Automatically generating suggestions is outside the initial scope.

## Intended workflow

1. Select a word or phrase, or place the cursor in a verse.
2. Right-click and find **Note suggestions**.
3. See the proposed corrections from the `Suggested:` fields of relevant unresolved QA notes.
4. Click a correction to replace its exact anchored word, sentence, or verse after the original text is revalidated. A separate **View source QA note** action opens the explanation.
5. Save the actual project Scripture through Paratext's supported USFM editing API. The revised project text must be used by subsequent exports and normal Send/Receive.

Exact replacement and saved USFM are confirmed requirements. They are not implemented yet. The existing interactive mockup demonstrates the earlier read-only design and must be updated before being used as an application preview.

Direct, dynamically populated submenu previews are the preferred outcome, subject to API verification. A single context-menu command opening a suggestions panel is a fallback and does not complete the direct-preview requirement.

See [PLAN.md](PLAN.md) for the interaction design, feasibility checks, milestones, and acceptance criteria.

## Development references

- [Official Paratext demo plugins](https://github.com/ubsicap/paratext_demo_plugins)
- [Context-menu example: Reference Plugin B](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginB/PluginB.cs)
- [Selection example: Reference Plugin J](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginJ/ControlJ.cs)
- [Project notes example: Reference Plugin L](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginL/ControlL.cs)

Keep real Scripture projects, notes, credentials, and installed Paratext binaries out of this repository. Development examples should use synthetic text.

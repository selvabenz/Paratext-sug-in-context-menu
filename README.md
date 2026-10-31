# Paratext sug in context menu

Show suggestions from existing Paratext project notes in the Scripture editor's right-click context menu.

Target: **Paratext 9.5.110.1 on Windows**.

## Status

Planning repository. No plugin has been built, installed, or tested in Paratext yet.

The initial interpretation is that suggestions already exist in project notes. Automatically generating suggestions is outside the initial scope. Confirm this interpretation before implementing suggestion extraction.

## Intended workflow

1. Select a word or phrase, or place the cursor in a verse.
2. Right-click and find **Note suggestions**.
3. See short previews of relevant unresolved notes for this project and passage.
4. Choose a preview to read the full note, its author, reference, and discussion.

Direct, dynamically populated submenu previews are the preferred outcome, subject to API verification. A single context-menu command opening a suggestions panel is a fallback and does not complete the direct-preview requirement.

See [PLAN.md](PLAN.md) for the interaction design, feasibility checks, milestones, and acceptance criteria.

## Development references

- [Official Paratext demo plugins](https://github.com/ubsicap/paratext_demo_plugins)
- [Context-menu example: Reference Plugin B](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginB/PluginB.cs)
- [Selection example: Reference Plugin J](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginJ/ControlJ.cs)
- [Project notes example: Reference Plugin L](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginL/ControlL.cs)

Keep real Scripture projects, notes, credentials, and installed Paratext binaries out of this repository. Development examples should use synthetic text.

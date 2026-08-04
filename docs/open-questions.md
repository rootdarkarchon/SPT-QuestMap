# Open questions to resolve from source or user

Do not block on questions that the supplied source can answer.

## Source-resolvable

- What is the exact 4.0.13 web metadata marker/interface?
- How are mod MVC controllers/pages/static assets registered?
- Which authorization policy should guard profile data?
- What is the exact profile enumeration API?
- Where are quest status, completed conditions, and task counters stored?
- Which helpers determine quest availability and active seasonal events?
- How are trader portraits exposed to the client?
- Are quest image paths directly browser-accessible through Kestrel?
- What is the exact output/deployment directory structure for a C# server mod?

## User-provided

- Installed SPT root path.
- Server restart command.
- Whether the page should use English only initially or the selected server/client locale.
- Optional sanitized profiles for edge-case testing.

Default to English for v1 if locale preference is not otherwise available, while keeping locale retrieval replaceable.

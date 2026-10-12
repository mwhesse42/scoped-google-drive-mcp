# scoped-google-drive-mcp

A small C#/.NET MCP server that gives an AI agent access to **one Google Drive folder and nothing else**.

Most Drive connectors sign in as you and can see your entire Drive. This one signs in as a Google Cloud **service account**, which can only see folders you explicitly share with it. On top of that, the server resolves every path from a single root folder, so the agent can't reach anything outside it.

About 330 lines of code. Three tools.

## Tools

| Tool | What it does |
|---|---|
| `list_folder` | Lists files and folders at a path, e.g. `/` or `/Projects/2026` |
| `read_file` | Reads a file as text. Google Docs and Slides come back as plain text, Sheets as CSV |
| `write_file` | Replaces the content of an **existing** text file or Google Doc |

There is deliberately no "create file" tool (see [Limits](#limits)).

## Two layers of scoping

1. **Identity.** The service account is its own identity, not you. Google only shows it what you've shared with its email address.
2. **Code.** Paths are walked one name at a time from `RootFolderId`. `..` is rejected. There is no search-by-ID or "list everything" call.

Either layer alone limits access; together, a misconfiguration in one is still contained by the other.

## Setup

### 1. Create the service account (Google Cloud Console)

1. Create or pick a project.
2. **APIs & Services → Library**: enable the **Google Drive API**.
3. **IAM & Admin → Service Accounts → Create**. No project roles needed.
4. On the account's **Keys** tab: **Add key → JSON**. Save it outside any synced folder. Treat it like a password.

### 2. Share one folder with it

In Google Drive, share the folder with the service account's email (`client_email` in the key file). Choose **Viewer** for read-only or **Editor** to allow `write_file`. Copy the folder ID from its URL (`drive.google.com/drive/folders/<id>`).

### 3. Configure

Copy `appsettings.example.json` somewhere outside the repo, fill in the key path and folder ID, and point `AI_WORKSPACE_CONFIG` at it (or put it next to the built exe as `appsettings.json`).

### 4. Build and register with Claude

```bash
dotnet build -c Release
```

In `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "scoped-drive": {
      "command": "C:\\path\\to\\scoped-drive-mcp\\src\\AI.Workspace.Mcp\\bin\\Release\\net10.0\\AI.Workspace.Mcp.exe",
      "env": {
        "AI_WORKSPACE_CONFIG": "C:\\Users\\<you>\\.config\\claude-drive\\appsettings.json"
      }
    }
  }
}
```

Fully quit and reopen Claude; it reads this file only at startup.

For Claude Code: `claude mcp add scoped-drive -e AI_WORKSPACE_CONFIG=<path> -- <path-to-exe>`

## Demo

1. Share a folder containing a couple of files with the service account.
2. Ask Claude: *"List my Drive."* It shows only that folder's contents.
3. Ask: *"Read /notes.md and fix the typos."* It reads, edits and saves the existing file.
4. Ask: *"Open my tax folder."* It can't, because that folder was never shared and is outside the root.

## Limits

- **No new files on a personal Gmail Drive.** Service accounts have no storage quota there, so creating or copying a file fails with *"The user's Drive storage quota has been exceeded."* Reading, editing and renaming existing files work. Workarounds: create files yourself and let the agent edit them, or use a Google Workspace Shared Drive (paid).
- `write_file` replaces the whole file. Formatting in a Google Doc is lost when it's rewritten as plain text.
- Sheets and Slides are read-only here.

## Options considered

| Option | Verdict |
|---|---|
| Google's official Drive connector | Full write access, but sees your whole Drive |
| Domain-wide delegation | Workspace-only, and it impersonates you, which means whole-Drive access again |
| `drive.file` scope + Google Picker | The right model for per-file consent, but needs a Picker UI the connector doesn't have |
| Workspace Shared Drives | Fixes file creation; requires a paid Workspace plan |
| **Service account + folder share** | **Chosen**: free, scoped, works today, can't create files |

## Optional: OAuth instead of a service account

If `ServiceAccountKeyFile` is empty, the server falls back to a personal OAuth sign-in (`CredentialsFile` + `TokenDirectory`). The code still confines paths to `RootFolderId`, but the token itself can see your whole Drive, so only the code layer protects you.

## Built with

.NET 10 · [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 1.4 · Google.Apis.Drive.v3

## License

MIT

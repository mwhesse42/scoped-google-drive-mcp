# scoped-google-drive-mcp

[![CI](https://github.com/mwhesse42/scoped-google-drive-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/mwhesse42/scoped-google-drive-mcp/actions/workflows/ci.yml)

A small C#/.NET MCP server that gives an AI agent access to **only the Google Drive files and folders you share with it**, and nothing else in your Drive.

Most Drive connectors sign in as you and can see your entire Drive. This one signs in as its own Google identity, a **service account** (think of it as a separate "Claude" user with its own email address). It starts with access to nothing. You grant access the same way you would to a colleague: share a folder or file with that email address.

About 400 lines of code, three tools, and unit tests for the rules that keep it scoped.

## Why I built this

I wanted Claude to work with files in my Google Drive, but I didn't want to give it access to *all* of my Google Drive. The standard connectors ask for the whole thing: every document, every folder, everything ever shared with me. I only wanted it to see the things I chose.

It was also something fun to try. I wanted to see how small an MCP server could be in C#/.NET, and whether I could get least-privilege access to Drive without paying for Google Workspace. It turned out to be possible, with one real limit, which is described below.

## Tools

| Tool | What it does |
|---|---|
| `list_folder` | `/` lists everything shared with the Claude account; a path like `/Projects/2026` lists inside a shared folder |
| `read_file` | Reads a file as text. Google Docs and Slides come back as plain text, Sheets as CSV. Binary files (PDFs, images) are refused, and so is anything over 1 MB |
| `write_file` | Replaces the content of an **existing** text file or Google Doc. Not offered at all in read-only mode |

There is deliberately no "create file" tool (see [Limits](#limits)).

## What the agent can access

Access is controlled entirely by **Drive sharing**. Whatever is shared with the service account's email address, the agent can reach. Nothing else.

| You share... | The agent can reach... |
|---|---|
| A folder | That folder, every file and subfolder inside it, and anything added to it later |
| A single file | Just that file |
| Several folders and files | All of them; each appears at the top level under `/` |
| Nothing | Nothing. `/` comes back empty |

- **Viewer or Editor decides what it can do.** With Viewer, the agent can list and read. With Editor, it can also use `write_file` on existing files.
- **Revoking access is just unsharing.** Remove the service account from a folder's sharing settings and the agent loses it on its next call. No restart or code change needed.
- **Anyone can share with it.** If someone else shares a file with the service account's email, the agent can see that too. Keep the email address to yourself.
- **Changes can take a minute.** Sharing usually takes effect within seconds, but occasionally a minute or two.

### Optional extra lock: `RootFolderId`

If you set `RootFolderId` in the config, the server treats that one folder as `/` and ignores everything else that's shared. Google still allows more, but this server won't use it. Leave it empty to use sharing alone.

### Optional: read-only mode

Set `"ReadOnly": true` and the server asks Google only for read-only Drive permission, and doesn't register `write_file` at all, so the agent never sees a write tool. Even if a folder is shared as Editor, this server can't change it.

### How it's enforced

1. **Identity (Google).** The service account isn't you. Google returns only what has been shared with it. This is the real boundary.
2. **Permission requested.** In read-only mode the server asks Google for read-only access, so even an Editor share can't be used to write.
3. **Code (this server).** Every path is resolved one name at a time from `/`. `..` is rejected, and there's no way to open a file by its ID, so the agent can't jump to something it found elsewhere. If two items in a folder share a name, the server returns an error rather than guessing which one was meant.

## Setup

You need a Google account, about 15 minutes, and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

### 1. Create a Google Cloud project

1. Go to [console.cloud.google.com](https://console.cloud.google.com/) and sign in with your Google account.
2. Click the project picker at the top → **New project**. Name it anything (e.g. `claude-drive`) → **Create**.
3. Make sure the new project is selected in the project picker.

There's no charge for this. The Drive API is free at personal-use volumes.

### 2. Enable the Drive API

1. **APIs & Services → Library**.
2. Search for **Google Drive API** → **Enable**.

That's the only API needed. Docs, Sheets and Slides are read through Drive's export feature.

### 3. Create the service account (the "Claude user")

1. **IAM & Admin → Service Accounts → Create service account**.
2. Name: e.g. `claude-drive-access`. Google generates an email address such as `claude-drive-access@claude-drive.iam.gserviceaccount.com`. **This email address is the Claude user you'll share files with.**
3. Skip the optional "grant access to project" and "grant users access" steps. It needs no project roles, because access is granted through Drive sharing instead.
4. Click **Done**.

### 4. Create a key (how the server signs in)

1. Open the service account → **Keys** tab → **Add key → Create new key → JSON → Create**.
2. A `.json` file downloads. It contains the account's private key.
3. Move it somewhere private, for example `C:\Users\<you>\.config\claude-drive\service-account.json`.

**Treat this file like a password.** Anyone who has it can act as the Claude user. Don't put it in this repo, a synced folder, or the Drive folder itself, and never paste its contents into a chat. If it leaks, delete the key in the Keys tab and create a new one.

### 5. Share files and folders with the Claude user

1. In Google Drive, right-click a folder or file → **Share**.
2. Paste the service account's email address (also in the key file as `client_email`).
3. Choose **Viewer** (read only) or **Editor** (read and write).
4. Untick **Notify people** (the address can't receive email) → **Share**.

Repeat for anything else you want the agent to reach.

### 6. Configure the server

Copy `appsettings.example.json` to somewhere outside the repo, e.g. `C:\Users\<you>\.config\claude-drive\appsettings.json`, and set the key path:

```json
{
  "GoogleDrive": {
    "ServiceAccountKeyFile": "C:\\Users\\<you>\\.config\\claude-drive\\service-account.json",
    "RootFolderId": "",
    "ReadOnly": false
  }
}
```

Set `ReadOnly` to `true` if the agent should only read. Leave `RootFolderId` empty to allow everything shared, or set it to one folder's ID (the last part of its URL, `drive.google.com/drive/folders/<id>`) to lock the server to that folder.

### 7. Build and register with Claude

```bash
dotnet build -c Release
dotnet test
```

In Claude's desktop config file (**Settings → Developer → Edit Config**), add:

```json
{
  "mcpServers": {
    "scoped-drive": {
      "command": "C:\\path\\to\\scoped-google-drive-mcp\\src\\ScopedDriveMcp\\bin\\Release\\net10.0\\ScopedDriveMcp.exe",
      "env": {
        "SCOPED_DRIVE_CONFIG": "C:\\Users\\<you>\\.config\\claude-drive\\appsettings.json"
      }
    }
  }
}
```

Fully quit and reopen Claude. It reads this file only at startup.

For Claude Code: `claude mcp add scoped-drive -e SCOPED_DRIVE_CONFIG=<path> -- <path-to-exe>`

### 8. Check it works

Ask Claude: *"List my Drive."*

- **Working:** you see only what you shared. An empty shared folder still counts: if the folder itself shows up, sign-in and sharing both work.
- **Not working:** an error about the key file or config means a path is wrong. An empty `/` usually means sharing hasn't taken effect yet (wait a minute) or you shared with a different email address.

### Test without Claude

A small console app uses the same config and Drive code as the server, so you can check your key and sharing directly:

```bash
set SCOPED_DRIVE_CONFIG=C:\Users\<you>\.config\claude-drive\appsettings.json

dotnet run --project tools/ScopedDriveMcp.Console -- list
dotnet run --project tools/ScopedDriveMcp.Console -- list /Projects
dotnet run --project tools/ScopedDriveMcp.Console -- read /Projects/notes.md
dotnet run --project tools/ScopedDriveMcp.Console -- write /Projects/notes.md local-notes.md
```

If `list` shows what you shared, the server will see the same thing.

### Rotating the key

There's no "re-authenticate" step for a service account. To replace a key: create a new one on the Keys tab, point `ServiceAccountKeyFile` at it, restart Claude, then delete the old key.

## Demo

1. Share a folder with a couple of files in it with the Claude user.
2. Ask Claude: *"List my Drive."* It shows only that folder.
3. Ask: *"Read notes.md in that folder and fix the typos."* It reads, edits and saves the existing file.
4. While Claude is open, share a second file with the Claude user, then ask it to list again. The new file appears.
5. Unshare the folder and ask again. It's gone.
6. Ask: *"Open my tax folder."* It can't, because that folder was never shared.

## Limits

- **No new files on a personal Gmail Drive.** Service accounts have no storage quota there, so creating or copying a file fails with *"The user's Drive storage quota has been exceeded."* Reading, editing and renaming existing files work. Workarounds: create files yourself and let the agent edit them, or use a Google Workspace Shared Drive (paid).
- `write_file` replaces the whole file. Formatting in a Google Doc is lost when it's rewritten as plain text.
- Sheets and Slides are read-only here.

## Options considered

| Option | Scoped? | Can create files? | Cost | Verdict |
|---|---|---|---|---|
| **Service account + folder share** | Yes | No | Free | **Chosen** |
| Google's official Drive connector | No | Yes | Free | Rejected |
| Domain-wide delegation | No | Yes | Workspace | Rejected |
| `drive.file` scope + Google Picker | Yes | Yes | Free | Not available |
| Workspace Shared Drives | Yes | Yes | Workspace | Too costly for this use |
| Separate dedicated Gmail account | Yes | Manually | Free | Possible, not tested |

**Service account + folder share (chosen).** It's free, works on a personal Gmail account, and access is limited to the folders and files you share with it. Reading, editing and renaming all work. The one gap is creating files. The workaround: create the file yourself, and let the agent fill it in.

**Google's official Drive connector (rejected).** It's built by Google and has full write access with no quota problem, because it acts as you. But it requests broad Drive access rather than folder-level access, which defeats the point.

**Domain-wide delegation / impersonation (rejected).** This lets a service account act as a real user, including creating files. It's only available on Google Workspace. Even there, it gives the service account that user's entire Drive, which is the same problem as the official connector.

**`drive.file` scope + Google Picker (not available).** This is Google's own design for scoped access: the app only sees files you pick in a Picker dialog, and file creation works because it's your real account. It's the best model on paper, but the connector has to implement the Picker sign-in flow, and none of the options available to me did.

**Workspace Shared Drives (too costly for this use).** Files in a Shared Drive belong to the drive, not a person, so a service account can create files there without hitting the quota limit. Shared Drives need a paid Google Workspace subscription, though. That's worth it only if the agent needs to create files often.

**Separate dedicated Gmail account (possible, not tested).** Create a second free Gmail account that holds only the folder you share with the agent. You create new files by signing in to that account, and your main Drive stays out of reach. It's free, but you have to manage a second account and switch to it whenever you need a new file.

## Optional: OAuth instead of a service account

If `ServiceAccountKeyFile` is empty, the server falls back to signing in as you with OAuth (`CredentialsFile` + `TokenDirectory`), which opens a browser the first time. Your own token can see your whole Drive, so in this mode set `RootFolderId`: the code then confines every path to that folder, but only the code is protecting you. The service account is the recommended setup.

## Project layout

```
src/ScopedDriveMcp/
  Program.cs          Host setup; registers the write tool only when not read-only
  DriveTools.cs       The three MCP tools
  DriveWorkspace.cs   Path resolution, read and write rules (no Google types)
  IDriveClient.cs     The three Drive calls the server needs: list, download, upload
  GoogleDriveClient.cs  Real IDriveClient: sign-in and Google Drive API calls
  DrivePolicy.cs      Pure rules: query building and escaping, which files can be read or written, duplicate names
  DriveSettings.cs    Config loading
  WorkspacePath.cs    Path normalisation and traversal checks
tools/ScopedDriveMcp.Console/
  Console app to list, read and write directly, without Claude
tests/ScopedDriveMcp.Tests/
  Unit tests; DriveWorkspace is tested against a Moq mock of IDriveClient
```

`DriveWorkspace` depends on a narrow `IDriveClient` interface (list, download, upload) rather than Google's `DriveService`, which can't be mocked. `GoogleDriveClient` is the real implementation, registered through dependency injection; the tests inject a Moq mock instead. That covers path walking, duplicate names, size and type checks, and read-only refusal without calling Google.

## Built with

.NET 10 · [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 1.4 · Google.Apis.Drive.v3

## License

MIT

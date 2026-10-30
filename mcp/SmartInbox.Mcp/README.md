# SmartInbox.Mcp

An [MCP](https://modelcontextprotocol.io) server for Smart Inbox. It lets Claude (Claude Desktop, Claude Code, or any MCP client) read, explain, triage and route official correspondence, with exactly the permissions of your API key.

```bash
dotnet tool install -g SmartInbox.Mcp
```

## Use it with Claude Code

```bash
claude mcp add smart-inbox --env SMARTINBOX_URL=https://inbox.example.com --env SMARTINBOX_API_KEY=sik_... -- smartinbox-mcp
```

## Use it with Claude Desktop

`claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "smart-inbox": {
      "command": "smartinbox-mcp",
      "env": {
        "SMARTINBOX_URL": "https://inbox.example.com",
        "SMARTINBOX_API_KEY": "sik_...",
        "SMARTINBOX_MCP_WRITES": "false"
      }
    }
  }
}
```

Create the key in Smart Inbox under **Administration → API keys**. Give it a name like "Claude (Sara)" so the document timeline shows who delegated to Claude, and the narrowest role that does the job.

## What Claude can do

| Read (always on) | Write (only with `SMARTINBOX_MCP_WRITES=true`) |
|---|---|
| `needs_attention`, `list_documents`, `get_document`, `read_document_text`, `list_departments`, `get_workload`, `get_stats`, `list_reply_templates`, `get_reply_drafts`, `search_register` | `route_document`, `acknowledge_document`, `close_document`, `reopen_document`, `reanalyze_document`, `add_comment`, `submit_text`, `draft_reply` (no approve tool: people approve in the web app) |

Plus resources (`smartinbox://documents/{id}`, `.../text`, `smartinbox://departments`) and prompts (`triage_inbox`, `daily_briefing`, `explain_document`).

Try: *"Use the triage_inbox prompt"*, *"What's overdue for Legal Affairs?"*, *"Explain the latest court notice in English and Arabic."*

## Safety

- **No privileges of its own.** Every call uses your API key; the Smart Inbox API enforces the key's role and department.
- **Read-only by default.** Write tools aren't even listed unless enabled, and they carry MCP hints (`destructiveHint` on route and close) so clients ask before running them. Route and close require a note, which is saved on the timeline.
- **Prompt-injection aware.** Letters come from outside: document text is returned inside `<untrusted_document_text>`, document-derived fields sit under `fromDocument`, and the server instructions tell the model to treat both as data and to report, not follow, any instructions inside.
- **Brakes.** At most 20 writes per key per minute (`SMARTINBOX_MCP_MAX_WRITES_PER_MINUTE`). Every tool call is logged to stderr (tool, key prefix, document id, outcome; never document text).

## Settings

| Variable | Default | |
|---|---|---|
| `SMARTINBOX_URL` | `http://localhost:5080` | Smart Inbox address |
| `SMARTINBOX_API_KEY` | – | Your key (stdio mode) |
| `SMARTINBOX_MCP_WRITES` | `false` | Enable write tools |
| `SMARTINBOX_MCP_MAX_TEXT` | `8000` | Characters per `read_document_text` slice |
| `SMARTINBOX_MCP_MAX_WRITES_PER_MINUTE` | `20` | Write brake per key |
| `SMARTINBOX_MCP_PORT` | `8090` | HTTP mode port |

## Shared (HTTP) mode

`smartinbox-mcp --http` serves Streamable HTTP at `/mcp`. It holds no key: each client sends its own `Authorization: Bearer sik_...`, and requests without one get `401`. Run it behind HTTPS.

More: `docs/MCP.md` in the Smart Inbox repository.

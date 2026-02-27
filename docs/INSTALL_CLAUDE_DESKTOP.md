# Installing Claude Desktop

Claude Desktop is a Windows application from Anthropic that gives you a chat interface to Claude, similar to ChatGPT. Once installed and connected to the OpticStudio MCP Server, you can control Zemax OpticStudio through natural language conversation.

---

## Step 1: Create an Anthropic Account

1. Go to [claude.ai](https://claude.ai)
2. Click **Sign up**
3. Create an account using your email address or Google account
4. You will need a **paid plan** (Pro or Team) to use MCP servers with Claude Desktop

---

## Step 2: Download Claude Desktop

1. Go to [claude.ai/download](https://claude.ai/download)
2. Click **Download for Windows**
3. Run the installer (`Claude-Setup.exe`)
4. Follow the installation prompts
5. Claude Desktop will appear in your Start menu and system tray

---

## Step 3: Sign In

1. Open Claude Desktop from the Start menu
2. Sign in with the Anthropic account you created in Step 1
3. You should see a chat interface where you can type messages to Claude

---

## Step 4: Connect the OpticStudio MCP Server

You have two options: use the **ConfigureClaudeMCP** utility (easiest) or configure manually.

### Option A: Using ConfigureClaudeMCP (Recommended)

1. Make sure you have [built the solution](../README.md#step-1-build-the-solution) first
2. Navigate to `src\ConfigureClaudeMCP\bin\Debug\` in the repository
3. Run **ConfigureClaudeMCP.exe**
4. The tool will auto-detect `ZemaxMCP.Server.exe` (if not found, click **Browse** and locate it in `src\ZemaxMCP.Server\bin\Debug\net48\`)
5. Click **Configure Claude Desktop**
6. You should see a green status message: *"Claude Desktop configured successfully"*
7. **Close and reopen Claude Desktop** (the MCP server config is only read at startup)

### Option B: Manual Configuration

If you prefer to configure manually:

1. **Close Claude Desktop** completely (right-click the system tray icon and choose **Quit**)

2. Open File Explorer and navigate to:
   ```
   %APPDATA%\Claude
   ```
   (Type this into the File Explorer address bar and press Enter. This usually resolves to `C:\Users\YourName\AppData\Roaming\Claude`)

3. If the file `claude_desktop_config.json` exists, open it in Notepad. If it doesn't exist, create a new text file with that exact name.

4. Add the following content (adjust the path to match where you built the server):

   ```json
   {
     "mcpServers": {
       "zemax-mcp": {
         "command": "C:\\GIT\\OpticStudioMCPServer\\src\\ZemaxMCP.Server\\bin\\Debug\\net48\\ZemaxMCP.Server.exe",
         "args": []
       }
     }
   }
   ```

   **Important:** Use double backslashes (`\\`) in the file path, not single backslashes.

   If the file already has content (other MCP servers), add the `zemax-mcp` entry inside the existing `mcpServers` block. Do not duplicate the outer braces.

5. Save the file and **reopen Claude Desktop**

---

## Step 5: Verify It Works

1. Open Claude Desktop
2. Look for a small hammer/tools icon near the text input box - this indicates MCP tools are available
3. Type: **"Connect to OpticStudio and show me the system data"**
4. Claude should call the `zemax_connect` tool and display your optical system

If you don't see the tools icon, check the [Troubleshooting](#troubleshooting) section below.

---

## Step 6: Disable Tool Permission Prompts (Recommended)

By default, Claude Desktop will ask you to approve **every single tool call** before it runs. This means each time Claude wants to connect to OpticStudio, read a surface, or run an analysis, a permission popup will appear and you must click **Allow**. This quickly becomes tedious when working with optical designs that require many tool calls in sequence.

You can configure Claude Desktop to automatically allow all OpticStudio MCP tools so the workflow is uninterrupted.

### How to Allow All Tools

1. **Close Claude Desktop** completely (right-click the system tray icon > **Quit**)

2. Open the config file in Notepad:
   ```
   %APPDATA%\Claude\claude_desktop_config.json
   ```

3. Add `"autoApprove"` to the `zemax-mcp` server entry with a list of tool name patterns. Use `"zemax_*"` to allow all OpticStudio tools at once:

   ```json
   {
     "mcpServers": {
       "zemax-mcp": {
         "command": "C:\\GIT\\OpticStudioMCPServer\\src\\ZemaxMCP.Server\\bin\\Debug\\net48\\ZemaxMCP.Server.exe",
         "args": [],
         "autoApprove": [
           "zemax_*"
         ]
       }
     }
   }
   ```

4. Save the file and **reopen Claude Desktop**

Now Claude will call all OpticStudio tools without asking for permission each time.

### Allow Only Specific Tools

If you prefer to only auto-approve certain tools (for example, read-only tools) and still be prompted for others, list them individually:

```json
"autoApprove": [
  "zemax_connect",
  "zemax_status",
  "zemax_get_system",
  "zemax_get_surface",
  "zemax_spot_diagram",
  "zemax_fft_mtf",
  "zemax_cardinal_points",
  "zemax_rms_spot"
]
```

With this configuration, Claude will auto-run the listed tools but still ask permission before modifying surfaces, optimizing, or saving files.

---

## How to Use

Once connected, you can interact with OpticStudio using natural language. Here are some example prompts:

- *"Connect to OpticStudio"*
- *"Open the file C:\Lenses\MyDoublet.zmx"*
- *"Show me the system data"*
- *"What are the Seidel coefficients?"*
- *"Calculate the MTF at 50 cycles/mm"*
- *"Set surface 2 radius to 100mm and make it variable"*
- *"Optimize the system for minimum RMS spot size"*
- *"Run a spot diagram for all fields"*
- *"Save the file"*

Claude will call the appropriate OpticStudio tools automatically based on your requests.

---

## Troubleshooting

### No tools icon appears in Claude Desktop

- Make sure you edited the correct config file: `%APPDATA%\Claude\claude_desktop_config.json`
- Verify the path to `ZemaxMCP.Server.exe` is correct and the file exists
- Make sure you completely closed and reopened Claude Desktop after editing the config
- Check that the JSON syntax is valid (no missing commas or brackets)

### "Connection failed" errors

- Make sure OpticStudio is installed and licensed on your machine
- Run [FixBinaries](../README.md#step-2-fix-binaries-configure-zos-api-path) first to configure the ZOS-API path, then rebuild

### Claude says it can't find the tools

- Restart Claude Desktop (right-click tray icon > Quit, then reopen)
- Check the server log files in `src\ZemaxMCP.Server\bin\Debug\net48\logs\` for error messages

### Where to get help

- For Claude Desktop issues: [Anthropic Support](https://support.anthropic.com)
- For OpticStudio MCP Server issues: Check the [README troubleshooting section](../README.md#troubleshooting)

# Installing Claude Code

Claude Code is a command-line tool from Anthropic that runs in your terminal. It is designed for developers and power users who prefer working in a terminal rather than a graphical chat window. Once connected to the OpticStudio MCP Server, you can control Zemax OpticStudio through natural language commands directly in your terminal.

---

## Step 1: Create an Anthropic Account

1. Go to [claude.ai](https://claude.ai)
2. Click **Sign up**
3. Create an account using your email address or Google account
4. You will need a **paid plan** (Max or Team) to use Claude Code

---

## Step 2: Install Node.js

Claude Code requires Node.js (a JavaScript runtime) to be installed.

1. Go to [nodejs.org](https://nodejs.org)
2. Download the **LTS** (Long Term Support) version for Windows
3. Run the installer and accept all defaults
4. To verify the installation, open a new **Command Prompt** or **PowerShell** window and type:
   ```
   node --version
   ```
   You should see a version number like `v22.x.x`

---

## Step 3: Install Claude Code

1. Open a **Command Prompt** or **PowerShell** window
2. Run this command:
   ```
   npm install -g @anthropic-ai/claude-code
   ```
3. Wait for the installation to complete
4. Verify it installed correctly:
   ```
   claude --version
   ```
   You should see a version number

---

## Step 4: Sign In to Claude Code

1. Open a **Command Prompt** or **PowerShell** window
2. Type:
   ```
   claude
   ```
3. On first launch, Claude Code will open your web browser to sign in with your Anthropic account
4. After signing in, return to the terminal - Claude Code is ready to use
5. Type `/quit` to exit for now (we need to configure the MCP server first)

---

## Step 5: Connect the OpticStudio MCP Server

You have two options: use the **ConfigureClaudeMCP** utility (easiest) or configure manually.

### Option A: Using ConfigureClaudeMCP (Recommended)

1. Make sure you have [built the solution](../README.md#step-1-build-the-solution) first
2. Navigate to `src\ConfigureClaudeMCP\bin\Debug\` in the repository
3. Run **ConfigureClaudeMCP.exe**
4. The tool will auto-detect `ZemaxMCP.Server.exe` (if not found, click **Browse** and locate it in `src\ZemaxMCP.Server\bin\Debug\net48\`)
5. Click **Configure Claude Code**
6. You should see a green status message: *"Claude Code configured successfully"*

### Option B: Manual Configuration (Command Line)

1. Open a **Command Prompt** or **PowerShell** window
2. Run this command (adjust the path to match where you built the server):

   ```
   claude mcp add --transport stdio --scope user zemax-mcp -- "C:\GIT\OpticStudioMCPServer\src\ZemaxMCP.Server\bin\Debug\net48\ZemaxMCP.Server.exe"
   ```

3. To verify it was added successfully:
   ```
   claude mcp list
   ```
   You should see `zemax-mcp` in the list.

---

## Step 6: Verify It Works

1. Open a terminal and type:
   ```
   claude
   ```
2. At the Claude prompt, type: **"Connect to OpticStudio and show me the system data"**
3. Claude should call the `zemax_connect` tool and display your optical system
4. The first connection may take 10-20 seconds while OpticStudio starts up

---

## Step 7: Disable Tool Permission Prompts (Recommended)

By default, Claude Code will ask you to approve **every single tool call** before it runs. This means each time Claude wants to connect to OpticStudio, read a surface, or run an analysis, you must press **y** (yes) to allow it. This quickly becomes tedious when working with optical designs that require many tool calls in sequence.

You can configure Claude Code to automatically allow all OpticStudio MCP tools so the workflow is uninterrupted.

### Option A: Allow All Tools at Once (Quickest)

Run this command in your terminal:

```
claude config set --global allowedTools "mcp__zemax-mcp__*"
```

This tells Claude Code to automatically approve any tool from the `zemax-mcp` server without prompting. The `*` wildcard matches all tool names.

If you already have other allowed tools configured, you can add to the list:

```
claude config add --global allowedTools "mcp__zemax-mcp__*"
```

### Option B: Allow Tools Interactively During a Session

While chatting with Claude Code, you can type:

```
/allowed-tools
```

This opens an interactive menu where you can select which tools to auto-approve. Choose the `zemax-mcp` tools you want to allow. Your selections are saved for future sessions.

### Option C: Allow Only Specific Tools

If you prefer to only auto-approve certain tools (for example, read-only tools) and still be prompted for others, add them individually:

```
claude config add --global allowedTools "mcp__zemax-mcp__zemax_connect"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_status"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_get_system"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_get_surface"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_spot_diagram"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_fft_mtf"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_cardinal_points"
claude config add --global allowedTools "mcp__zemax-mcp__zemax_rms_spot"
```

With this configuration, Claude will auto-run the listed tools but still ask permission before modifying surfaces, optimizing, or saving files.

### Check Your Current Allowed Tools

```
claude config get allowedTools
```

### Remove All Allowed Tools (Re-enable Prompts)

```
claude config set --global allowedTools ""
```

---

## How to Use

Claude Code runs as an interactive chat in your terminal. Type your requests in natural language:

- *"Connect to OpticStudio"*
- *"Open the file C:\Lenses\MyDoublet.zmx"*
- *"Show me the system data"*
- *"What are the Seidel coefficients?"*
- *"Calculate the MTF at 50 cycles/mm"*
- *"Set surface 2 radius to 100mm and make it variable"*
- *"Optimize the system for minimum RMS spot size"*
- *"Run a spot diagram for all fields"*
- *"Save the file"*

### Useful Commands

| Command | What it does |
|---------|--------------|
| `/quit` | Exit Claude Code |
| `/clear` | Clear the conversation history |
| `/help` | Show help information |
| `Ctrl+C` | Cancel the current operation |
| `Escape` | Clear the current input |

### Tips for Optical Engineers

- **Claude will ask for permission** before calling tools unless you have [disabled permission prompts](#step-7-disable-tool-permission-prompts-recommended). You can approve individual calls by pressing **y**, or auto-approve tools to speed up your workflow.
- You can paste file paths directly: *"Open C:\Users\Me\Documents\Lenses\triplet.zmx"*
- Ask Claude to explain results: *"Run a spot diagram and explain what the results mean"*
- Chain operations: *"Set up a doublet with BK7 and SF2, set the aperture to F/4, add 3 fields at 0, 10, and 20 degrees, optimize for RMS spot size, then show me the MTF"*
- Claude remembers context within a session, so you can say things like *"now try with SF11 instead"*

---

## Managing the MCP Server

### Remove the server

```
claude mcp remove --scope user zemax-mcp
```

### Check registered servers

```
claude mcp list
```

### Update the server path (if you rebuild to a different location)

Remove and re-add:
```
claude mcp remove --scope user zemax-mcp
claude mcp add --transport stdio --scope user zemax-mcp -- "C:\new\path\to\ZemaxMCP.Server.exe"
```

---

## Troubleshooting

### "npm is not recognized as a command"

Node.js was not installed correctly or the terminal was not restarted after installation.
- Close all terminal windows
- Open a **new** Command Prompt or PowerShell
- Try `node --version` again
- If it still fails, reinstall Node.js from [nodejs.org](https://nodejs.org) and make sure "Add to PATH" is checked during installation

### "claude is not recognized as a command"

Claude Code was not installed correctly or the terminal needs to be restarted.
- Close and reopen your terminal
- Try `claude --version`
- If it still fails, run `npm install -g @anthropic-ai/claude-code` again

### Claude doesn't see the OpticStudio tools

- Run `claude mcp list` to verify `zemax-mcp` is registered
- Make sure the path to `ZemaxMCP.Server.exe` is correct
- Run [FixBinaries](../README.md#step-2-fix-binaries-configure-zos-api-path) and rebuild if you haven't already

### "Connection failed" when connecting to OpticStudio

- Make sure OpticStudio is installed and licensed on your machine
- For standalone mode (default): no extra setup needed
- For extension mode: open OpticStudio first, go to **Programming > Interactive Extension**

### Where to get help

- For Claude Code issues: [github.com/anthropics/claude-code/issues](https://github.com/anthropics/claude-code/issues)
- For OpticStudio MCP Server issues: Check the [README troubleshooting section](../README.md#troubleshooting)

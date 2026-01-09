================================================================================
                    AIA - AI-Powered Personal Productivity Assistant
                                  Installation Guide
================================================================================

Thank you for choosing AIA! Please read the following information before 
proceeding with the installation.

--------------------------------------------------------------------------------
                              SYSTEM REQUIREMENTS
--------------------------------------------------------------------------------

  • Operating System: Windows 10 (Build 22000+) or Windows 11
  • Runtime: .NET 10.0 Runtime (will be installed if not present)
  • Disk Space: Approximately 150 MB
  • Memory: 4 GB RAM minimum (8 GB recommended)

Optional Requirements:
  • Microsoft Outlook - Required for Outlook plugin integration
  • Microsoft Teams - Required for Teams plugin integration

--------------------------------------------------------------------------------
                              WHAT WILL BE INSTALLED
--------------------------------------------------------------------------------

AIA will install the following components:

  1. AIA Application
     - Main productivity assistant application
     - System tray integration
     - Global hotkey support (default: Win+Q)

  2. Plugin System
     - Outlook Integration Plugin (optional)
     - Teams Integration Plugin (optional)

  3. Data Directories
     The following folders will be created in the installation directory:
     - ai-config/      : AI provider configurations
     - databanks/      : Data bank storage
     - screenshots/    : Saved screen captures
     - backups/        : Application backups
     - Plugins/        : Plugin assemblies

--------------------------------------------------------------------------------
                                KEY FEATURES
--------------------------------------------------------------------------------

  ✓ Task Management - Hierarchical tasks with priorities and due dates
  ✓ Smart Reminders - Desktop notifications with snooze functionality
  ✓ Data Banks - Organize documents, images, and text content
  ✓ Screen Capture - Full screen and window capture with quick save
  ✓ AI Chat - Multi-provider AI integration (OpenAI, Azure, Gemini, Claude)
  ✓ Plugin System - Extensible architecture for additional features

--------------------------------------------------------------------------------
                              AI PROVIDER SETUP
--------------------------------------------------------------------------------

To use AI features, you will need to configure at least one AI provider after 
installation. Supported providers:

  • OpenAI (GPT-5.2) - Requires API key from platform.openai.com
  • Azure AI Foundry - Requires Azure subscription and deployment
  • Google Gemini 3 - Requires API key from Google AI Studio
  • Anthropic Claude - Requires API key from console.anthropic.com

AI configuration can be accessed via Settings → Orchestration after launching 
the application.

--------------------------------------------------------------------------------
                              DATA PRIVACY
--------------------------------------------------------------------------------

  • All data is stored locally on your computer
  • No data is sent to external servers except when using AI features
  • AI conversations are sent to the configured AI provider's API
  • You control which AI providers have access to your data
  • Automatic backups can be configured in Settings → Data

--------------------------------------------------------------------------------
                              GETTING STARTED
--------------------------------------------------------------------------------

After installation:

  1. Launch AIA from the Start Menu or Desktop shortcut
  2. The application will minimize to the system tray
  3. Press Win+Q (or your custom hotkey) to open the overlay
  4. Configure AI providers in Settings → Orchestration (optional)
  5. Start creating tasks, reminders, and organizing your data!

For detailed documentation, visit: https://github.com/ffredyk/AIA

--------------------------------------------------------------------------------
                                  SUPPORT
--------------------------------------------------------------------------------

  GitHub Repository: https://github.com/ffredyk/AIA
  Report Issues: https://github.com/ffredyk/AIA/issues

--------------------------------------------------------------------------------
                                  LICENSE
--------------------------------------------------------------------------------

This software is open source. See the LICENSE file included with this 
installation for complete licensing terms.

================================================================================
                         Click "Next" to continue installation
================================================================================

# CrashDumpAnalyzer

CrashDumpAnalyzer is a tool designed to automatically analyze crash dump files and organize them based on their callstacks.

By grouping similar crashes together, it helps developers focus on analyzing unique crashes rather than reviewing every individual dump.

Additionally, CrashDumpAnalyzer provides a symbol server to resolve symbols during the analysis process.

**Important**: This project is intended for use within a company network only and should not be exposed to the internet. Set `UserAuthEnabled` to `true` to require sign-in for MVC/API controller endpoints; when it is `false`, those endpoints remain anonymous.
![mainpage](https://github.com/user-attachments/assets/304c935e-56d8-4a91-9f34-d33e202d00d3)
![dumppage](https://github.com/user-attachments/assets/ed4ff05e-051e-4a15-8827-4978999fe60e)

## Features
- dump files can be uploaded via web frontend or via REST-Api
- the upload overview stacks on narrow screens, and wide data tables can be scrolled horizontally without scrolling the whole page
- long crash lists scroll inside a panel capped at 70% of the viewport height, with sticky column headers and a keyboard-focusable scrolling region
- keyboard navigation includes visible focus outlines, a labeled dark-mode switch, and popovers opened with Enter or Space and dismissed with Escape; select rows and use Combine to link callstacks without dragging
- each new dump is analyzed in the background and assigned to a new or existing report entry matching its callstack
- duplicate dump uploads appear in the recent uploads list with a link to the original dump's callstack, without storing or analyzing another copy
- each dump has an expandable diagnostic report with OS/effective processor architecture, system/process uptime and virtual memory usage
- Users can add comments to report entries, set a version where the crash is fixed and set a ticket/issue number
- integration with Jira
- basic log file analysis 
  ![logfileqmlpage_dark](https://github.com/user-attachments/assets/821c34f2-5131-47bc-a1ad-263ce553c905)
  ![logfileqmlpage](https://github.com/user-attachments/assets/92089210-ff80-4c03-ae97-b28a9837db83)
- simple statistics page
  ![statisticspage](https://github.com/user-attachments/assets/0c8393b4-efba-4a14-a801-de53d7b9d788)

## Prerequisites
it is assumed that you can already analyze crash dumps of your application manually. This means you have set up a [symbol store](https://learn.microsoft.com/en-us/windows/win32/debug/using-symstore) where you store all necessary data for every build of your application. Also you should have configured your application with [SourceLink](https://github.com/dotnet/sourcelink).

CrashDumpAnalyzer needs access to your symbol store to properly analyze the crash dumps.



## Development

Controllers handle HTTP validation and responses; scoped application services own the application logic:

- `DumpFileService`: uploaded-file metadata and download preparation.
- `CallstackService` and `DumpCleanupService`: mutations, linking, deletion, retention, and orphan cleanup.
- `AnalysisResultService`: persistence and matching of dump/log analysis results.
- `CallstackQueryService`: filtering, sorting, linked-callstack aggregation, and recent uploads.
- `HomePageService`: page models, statistics, grouping, and issue-tracker enrichment.

The singleton `DumpAnalysisQueue` captures filenames and IDs, not request services. Each executing job creates and disposes its own service scope. `QueuedHostedService` awaits workers and propagates shutdown cancellation to active work. `MaxNumOfParallelOperations` defaults to 1 and must be greater than zero. The queue is in-memory, not durable. Persistence and file-deletion failures are surfaced to HTTP error handling or worker logging rather than reported as successful mutations.

Run the regression tests with `dotnet test .\TestProject\TestProject.csproj -p:OpenApiGenerateDocuments=false`. Service tests use isolated SQLite databases and temporary directories; external debugger and issue-tracker services are not required. Disabling OpenAPI generation avoids starting the application to generate documents during test builds.

Dump analysis retains the crash stack, exception code, executable/product version, dump time/timezone, command line, and PEB environment information. Additional diagnostics use `vertarget`, `.effmach`, `.time`, `.exr -1`, `.ecxr`, `r`, `~* kL`, `!runaway 7`, `!address -summary`, and `lm v`. They are stored per dump, not used for crash grouping, and replaced on re-analysis. Existing dumps need re-analysis to populate the report; the database column is added automatically on application startup. Small minidumps may lack exception context, memory-region data, or thread timings (`!runaway` needs thread-time data, such as dumps created with `.dump /mt` or `.dump /ma`). The memory summary also requires OS symbols. Debugger errors remain visible rather than being presented as zero usage or missing threads. Registers in a dump without exception context reflect the debugger's current context. Diagnostics can contain sensitive paths and memory addresses; they have the same access controls as other dump details.

To also run the optional debugger integration tests, set `CDB_TEST_EXE` to the full path of `cdb.exe` before running the tests. They create and analyze their own temporary dump, including paths containing spaces. The no-symbol test runs offline; set `CDB_TEST_SYMBOL_PATH` to an OS symbol path to also verify successful memory-summary output.

Frontend dependencies are pinned in `CrashDumpAnalyzer\libman.json`. Restore them with Visual Studio's **Restore Client-Side Libraries** command. Only the referenced minified builds and their source maps are selected; keep the accompanying license files. jQuery validation remains available for Identity forms through `_ValidationScriptsPartial`.

Shared interactions and mutation requests live in `wwwroot\js\site.js`; theme initialization runs early from `theme.js` to avoid a light-theme flash. Upload, grouped-filter, and statistics scripts load only on their respective pages through the layout's `Scripts` section. Keep reusable presentation in `wwwroot\css\site.css`, using Bootstrap utilities where appropriate; inline styles are reserved for dynamic issue-tracker colors.

## Setting Up CrashDumpAnalyzer

- clone the repository, open solution in VisualStudio, right-click on Solution and click "Publish". This will build the CrashDumpAnalyzer.
- install the Windows SDK. Select only the debugging tools in the installer.
- assuming that CrashDumpAnalyzer gets installed in C:\CrashDumpAnalyzer, create the folder c:\CrashDumpAnalyzer\FileUpload
  you can of course chose other paths...
- copy the CrashDumpAnalyzer build folder to C:\CrashDumpAnalyzer
- on a fresh installation, start CrashDumpAnalyzer. Without a configured `ConnectionStrings:DefaultConnection`, the default browser automatically opens the local setup wizard once the server is listening, using the actual listener port (HTTPS is preferred when available). For a Windows service or another non-interactive session, open the setup URL logged by the application in a browser on the server. If browser launch fails, the URL is also logged for manual access. All non-local requests and normal application endpoints are blocked until setup is finished and the application is restarted. Use a direct connection, not a reverse proxy.
- enter a writable SQLite connection string (for example `Data Source=C:\CrashDumpAnalyzer\WebApplication.db`), choose whether to require sign-in, and configure SMTP for confirmation/reset emails. When authentication is enabled, setup creates a confirmed first user with the `Admin` role; the administrator password is hashed in the database, never saved in JSON. SMTP settings are saved but not tested by sending mail. Existing users are not replaced, and other registrations retain their existing permissions.
- setup saves `ConnectionStrings:DefaultConnection`, `UserAuthEnabled`, `Smtp`, and `Setup:Completed` to `appsettings.json` in the installation/content-root folder. Other JSON settings are preserved (comments are removed). The service account needs write permission there and to the database folder. Restrict access to this folder and `appsettings.json`, which contains SMTP credentials.
- restart the application/service after saving. Setup cannot be submitted again after completion. Existing installations with a connection string skip the wizard; to deliberately enter setup before starting an installation, set `Setup:Completed` to `false` in configuration. Do not use this to replace an existing user database. Environment-specific JSON, user secrets, environment variables, and command-line settings still override `appsettings.json`; remove conflicting overrides before setup.
- use **Settings** to edit analysis, static folder, log analyzer, issue tracker, SMTP, build type, and version settings. The page is available to signed-in users in the `Admin` role when authentication is enabled, or via a direct localhost request (loopback host and peer, without forwarded headers). Remote requests cannot access it when authentication is disabled. Local access does not require signing in. The page edits the base `appsettings.json` using JSON syntax: backslashes in regexes and paths must be escaped once (for example, `"\\d+"` represents the regex `\d+`). Settings not shown and blank credential fields are preserved; use the Clear checkbox to remove a credential. Saving changes removes JSON comments; an unchanged save leaves the file untouched. A writable installation folder is required. Protect the file and folder: credentials stored in JSON are not encrypted. Environment-specific JSON, user secrets, environment variables, and command-line arguments can override the file; these overrides are not shown or edited. Restart after saving or manually editing the base file: it no longer reloads while running, because some settings are consumed only at startup. See [appsettings_example.json](https://github.com/stefankueng/CrashDumpAnalyzer/blob/main/CrashDumpAnalyzer/appsettings_example.json) for available values. Database, authentication, and server settings remain manual or setup-wizard configuration.
- you should create a new user account under which CrashDumpAnalyzer will be running. That user account should have only write rights to the FileUpload and it's installation folder, nowhere else!
- now set the access rights and create a service for CrashDumpAnalyzer in Powershell:
- $acl = Get-Acl "C:\CrashDumpAnalyzer"
  $aclRuleArgs = "CDA-User", "Read,Write,ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow"
  $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($aclRuleArgs)
  $acl.SetAccessRule($accessRule)
  $acl | Set-Acl "C:\CrashDumpAnalyzer"
  New-Service -Name "CrashDumpAnalyzer" -BinaryPathName "C:\CrashDumpAnalyzer\CrashDumpAnalyzer.exe --contentRoot C:\CrashDumpAnalyzer" -Credential "CDA-User" -Description "Web service to analyze crash dumps" -DisplayName "CrashDumpAnalyzer" -StartupType Automatic
- and finally, start the service:
  Start-Service -Name CrashDumpAnalyzer
- if everything went well, CrashDumpAnalyzer should be reachable under https://localhost/ (or whatever url you've configured in appsettings.json)


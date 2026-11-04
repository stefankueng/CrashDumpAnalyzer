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
- each dump is analyzed in the background, and once analysis is finished either a new report entry is created or the dump file is assigned to an existing entry matching the callstack of the crash
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

Frontend dependencies are pinned in `CrashDumpAnalyzer\libman.json`. Restore them with Visual Studio's **Restore Client-Side Libraries** command. Only the referenced minified builds and their source maps are selected; keep the accompanying license files. jQuery validation remains available for Identity forms through `_ValidationScriptsPartial`.

Shared interactions and mutation requests live in `wwwroot\js\site.js`; theme initialization runs early from `theme.js` to avoid a light-theme flash. Upload, grouped-filter, and statistics scripts load only on their respective pages through the layout's `Scripts` section. Keep reusable presentation in `wwwroot\css\site.css`, using Bootstrap utilities where appropriate; inline styles are reserved for dynamic issue-tracker colors.

## Setting Up CrashDumpAnalyzer

- clone the repository, open solution in VisualStudio, right-click on Solution and click "Publish". This will build the CrashDumpAnalyzer.
- install the Windows SDK. Select only the debugging tools in the installer.
- assuming that CrashDumpAnalyzer gets installed in C:\CrashDumpAnalyzer, create the folder c:\CrashDumpAnalyzer\FileUpload
  you can of course chose other paths...
- copy the CrashDumpAnalyzer build folder to C:\CrashDumpAnalyzer
- create the file C:\CrashDumpAnalyzer\appsettings.json, copy the contents from [appsettings_example.json](https://github.com/stefankueng/CrashDumpAnalyzer/blob/main/CrashDumpAnalyzer/appsettings_example.json) and adjust the settings
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


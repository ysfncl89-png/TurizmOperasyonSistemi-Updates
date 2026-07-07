Manual update procedure for TurizmOperasyonSistemi

Goal: Provide a simple, safe manual update flow so users can download a zip and update the program without risking database corruption.

1) Prepare release (developer)
- Publish the application via Visual Studio (Release) or dotnet publish.
- Zip the publish folder contents (do NOT include the user's turizm.db). Name the zip like TurizmOperasyonSistemi-publish-1.0.0.3.zip.
- Include a short README.txt in the zip containing the version number and install steps.
- Upload the zip to GitHub Releases (or share via secure file share).

2) Distribute to users
- Give the user the ZIP and the README (or link to the GitHub Release).

3) User manual install steps (very short)
- Close the application on the machine.
- Backup database: copy %LOCALAPPDATA%\TurizmOperasyonSistemi\turizm.db to a safe location (e.g., Desktop).
- Extract the ZIP into the application installation folder (e.g., C:\Program Files\TurizmOperasyonSistemi). When prompted, overwrite files.
- Do NOT overwrite any existing turizm.db unless instructed.
- Start the application. If migration is required and the published exe runs migrations at startup, it will apply them automatically.
- Verify About -> Kurulu Uygulama Sürümü shows the new version.

4) Rollback plan
- If something goes wrong, close the app and restore the turizm.db from the backup.

5) Notes for developers
- Keep containsMigrations flag in latest.json up-to-date when publishing releases used with automatic update in the future.
- Keep Data\appsettings.json defaults (Update:Enabled=false) to prevent accidental auto-updates.


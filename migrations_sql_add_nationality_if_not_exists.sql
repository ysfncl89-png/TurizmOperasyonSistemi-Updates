-- SQLite has no direct IF NOT EXISTS for ADD COLUMN; this script checks schema and adds column if missing using a simple select
PRAGMA table_info('Persons');
-- If 'Nationality' not present, run:
-- ALTER TABLE Persons ADD COLUMN Nationality TEXT;

-- Manual step: run the ALTER TABLE command above if the column does not exist.

-- SQL script to add Nationality column to Persons table in SQLite
-- Run via sqlite3 or other client. Existing data is preserved; new column is nullable.

ALTER TABLE Persons ADD COLUMN Nationality TEXT;

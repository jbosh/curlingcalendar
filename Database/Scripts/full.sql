-- This file is used to create the database and must be fully valid sqlite. This file should be the current version of the
-- database if it was created from scratch. Upgrades should be made in different files.

CREATE TABLE __meta (
	key TEXT NOT NULL,
	value TEXT NOT NULL,

	PRIMARY KEY (key)
);


CREATE TABLE users (
	id INTEGER NOT NULL,
	fullname TEXT NOT NULL,

	PRIMARY KEY (id)
);

CREATE TABLE leagues (
	id INTEGER,
	name TEXT NOT NULL,
	
	PRIMARY KEY (id)
);

CREATE TABLE teams (
	id INTEGER PRIMARY KEY AUTOINCREMENT,
	name TEXT NOT NULL,
	league_id INT NOT NULL,

	UNIQUE (league_id, name)
);

CREATE TABLE team_members (
	user_id INTEGER NOT NULL,
	team_id INTEGER NOT NULL,

	PRIMARY KEY(user_id, team_id)
);

CREATE TABLE games (
	league_id TEXT NOT NULL,
	time TEXT NOT NULL,
	sheet TEXT NOT NULL,

	team_a INTEGER NOT NULL,
	team_b INTEGER NOT NULL,

	PRIMARY KEY (league_id, time, sheet)
);
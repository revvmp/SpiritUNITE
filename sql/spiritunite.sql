CREATE DATABASE IF NOT EXISTS spiritunite CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;

USE spiritunite;

CREATE TABLE IF NOT EXISTS `role` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    in_game_usr_name VARCHAR(32) NOT NULL,
    gender INT NOT NULL DEFAULT 0,
    role_avatar_info VARBINARY(4096) NOT NULL,
    newbie_state VARBINARY(4096) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_role_name (in_game_usr_name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pokemon` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

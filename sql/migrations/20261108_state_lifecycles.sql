USE spiritunite;

CREATE TABLE IF NOT EXISTS `battle_settlement` (
    settlement_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    state TINYINT UNSIGNED NOT NULL,
    payload LONGBLOB NOT NULL,
    updated_at BIGINT NOT NULL,
    last_error VARCHAR(2000) NOT NULL DEFAULT '',
    PRIMARY KEY (settlement_id, game_ulong_uid),
    KEY ix_battle_settlement_pending (state, updated_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_settlement` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    settlement_id CHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    battle_started_at BIGINT NOT NULL,
    stage_id INT NOT NULL,
    node_id INT NOT NULL,
    difficulty_level INT NOT NULL,
    claim_state TINYINT UNSIGNED NOT NULL,
    refresh_drops LONGTEXT NOT NULL,
    CONSTRAINT fk_role_pve_settlement_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

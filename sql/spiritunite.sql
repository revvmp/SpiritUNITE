CREATE DATABASE IF NOT EXISTS spiritunite CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;

USE spiritunite;

CREATE TABLE IF NOT EXISTS `account` (
    account_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_name VARCHAR(20) NULL,
    email VARCHAR(254) NULL,
    password_hash VARBINARY(64) NULL,
    password_salt VARBINARY(32) NULL,
    password_iterations INT NULL,
    guest_id_hash BINARY(32) NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP NULL,
    UNIQUE KEY uq_account_user_name (user_name),
    UNIQUE KEY uq_account_email (email),
    UNIQUE KEY uq_account_guest_id (guest_id_hash),
    CONSTRAINT ck_account_kind CHECK (
        (guest_id_hash IS NULL AND user_name IS NOT NULL AND email IS NOT NULL AND password_hash IS NOT NULL
            AND password_salt IS NOT NULL AND password_iterations IS NOT NULL)
        OR (guest_id_hash IS NOT NULL AND user_name IS NULL AND email IS NULL AND password_hash IS NULL
            AND password_salt IS NULL AND password_iterations IS NULL))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `account_token` (
    token_hash BINARY(32) NOT NULL PRIMARY KEY,
    account_id BIGINT UNSIGNED NOT NULL,
    expires_at BIGINT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_account_token_account (account_id),
    CONSTRAINT fk_account_token_account FOREIGN KEY (account_id) REFERENCES `account` (account_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    in_game_usr_name VARCHAR(32) NOT NULL,
    gender INT NOT NULL DEFAULT 0,
    role_total_exp INT NOT NULL DEFAULT 0 CHECK (role_total_exp >= 0),
    role_history_max_total_exp INT NOT NULL DEFAULT 0,
    role_avatar_info VARBINARY(4096) NOT NULL,
    newbie_state VARBINARY(4096) NOT NULL,
    follow_pokemon_id INT NOT NULL DEFAULT 0,
    using_lobby_bg_id INT NOT NULL DEFAULT 0,
    first_pokemon_id INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_role_name (in_game_usr_name),
    CONSTRAINT ck_role_history_max_total_exp CHECK (role_history_max_total_exp >= role_total_exp),
    CONSTRAINT fk_role_account FOREIGN KEY (game_ulong_uid) REFERENCES `account` (account_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pokemon` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    gain_time BIGINT NOT NULL DEFAULT 0,
    gain_channel INT NOT NULL DEFAULT 0,
    new_tag INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_item` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    item_type INT NOT NULL,
    item_id INT NOT NULL,
    gain_time BIGINT NOT NULL,
    time_limited BIGINT NOT NULL DEFAULT 0,
    new_tag INT NOT NULL DEFAULT 0,
    item_cnt INT NOT NULL DEFAULT 1,
    item_level INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (game_ulong_uid, item_type, item_id),
    CONSTRAINT ck_role_item_cnt CHECK (item_cnt >= 0),
    CONSTRAINT ck_role_item_level CHECK (item_level >= 0),
    CONSTRAINT fk_role_item_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_coin` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    coin_id INT NOT NULL,
    coin_val BIGINT NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (game_ulong_uid, coin_id),
    CONSTRAINT ck_role_coin_val CHECK (coin_val >= 0),
    CONSTRAINT fk_role_coin_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_shop_buy` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    shop_id INT NOT NULL,
    spec_sale_num INT NOT NULL,
    spec_sale_cycle_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, shop_id),
    CONSTRAINT fk_role_shop_buy_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_login_day` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    login_day DATE NOT NULL,
    PRIMARY KEY (game_ulong_uid, login_day),
    CONSTRAINT fk_role_login_day_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_activity_task_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id, period_start),
    CONSTRAINT fk_role_activity_task_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;


CREATE TABLE IF NOT EXISTS `role_profile_style` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    cur_head_icon_id INT NOT NULL,
    player_contestant_info VARBINARY(4096) NOT NULL,
    individual_label_info VARBINARY(256) NOT NULL,
    is_anonymity INT NOT NULL DEFAULT 0,
    is_hide_info INT NOT NULL DEFAULT 0,
    cur_canopy_id INT NOT NULL DEFAULT 0,
    head_type INT NOT NULL,
    cur_head_res_id INT NOT NULL DEFAULT 0,
    CONSTRAINT ck_role_profile_style_flags CHECK (is_anonymity IN (0, 1) AND is_hide_info IN (0, 1)),
    CONSTRAINT fk_role_profile_style_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_card_face_wear` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    card_face_id INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_card_face_wear_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS `role_pokemon_used` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    held_item_design_info VARBINARY(256) NOT NULL,
    last_carry_trainer_item_id INT NOT NULL DEFAULT 0,
    last_schedue_id INT NOT NULL DEFAULT 0,
    battle_not_use_badge INT NOT NULL DEFAULT 0,
    wear_paster_id INT NOT NULL DEFAULT 0,
    pokemon_total_point INT NOT NULL DEFAULT 0,
    pokemon_season_point INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_used_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_held_item_slot` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    held_item_slot_index INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, held_item_slot_index),
    CONSTRAINT fk_role_held_item_slot_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_badge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    cur_badge_schedue_id INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_role_badge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_badge_schedue` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    badge_schedue_id INT NOT NULL,
    schedue_name VARCHAR(64) NOT NULL DEFAULT '',
    schedue_data VARBINARY(256) NOT NULL,
    unlock_time_stamp BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, badge_schedue_id),
    CONSTRAINT fk_role_badge_schedue_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE DATABASE IF NOT EXISTS spiritunite CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;

USE spiritunite;

CREATE TABLE IF NOT EXISTS `account` (
    account_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_name VARCHAR(20) NULL,
    email VARCHAR(254) NULL,
    password_hash VARBINARY(64) NULL,
    password_salt VARBINARY(32) NULL,
    guest_id_hash BINARY(32) NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP NULL,
    UNIQUE KEY uq_account_user_name (user_name),
    UNIQUE KEY uq_account_email (email),
    UNIQUE KEY uq_account_guest_id (guest_id_hash),
    CONSTRAINT ck_account_kind CHECK (
        (guest_id_hash IS NULL AND user_name IS NOT NULL AND email IS NOT NULL AND password_hash IS NOT NULL
            AND password_salt IS NOT NULL)
        OR (guest_id_hash IS NOT NULL AND user_name IS NULL AND email IS NULL AND password_hash IS NULL
            AND password_salt IS NULL))
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

CREATE TABLE IF NOT EXISTS `role_pokemon_time_limited` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    gain_time BIGINT NOT NULL DEFAULT 0,
    gain_channel INT NOT NULL DEFAULT 0,
    new_tag INT NOT NULL DEFAULT 0,
    time_limited BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_time_limited_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
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
    coin_seq BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (game_ulong_uid, coin_id),
    UNIQUE KEY uk_role_coin_seq (coin_seq),
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

CREATE TABLE IF NOT EXISTS `role_newbie_gift` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    gift_id INT NOT NULL,
    fetch_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, gift_id),
    CONSTRAINT fk_role_newbie_gift_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
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

CREATE TABLE IF NOT EXISTS `role_activity_task_finish` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    finish_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id, period_start),
    CONSTRAINT fk_role_activity_task_finish_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_activity_exchange` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    exchange_id INT NOT NULL,
    done_count INT NOT NULL DEFAULT 0,
    notify INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, exchange_id),
    CONSTRAINT fk_role_activity_exchange_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_unite_challenge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    is_click_start INT NOT NULL DEFAULT 0,
    click_start_time BIGINT NOT NULL DEFAULT 0,
    round_cnt INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, period_id),
    CONSTRAINT fk_role_unite_challenge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_unite_challenge_battle` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    battle_index INT NOT NULL,
    battle_status INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, period_id, battle_index),
    CONSTRAINT fk_role_unite_challenge_battle_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_unite_challenge_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    reward_index INT NOT NULL,
    reward_status INT NOT NULL,
    reward_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, period_id, reward_index),
    CONSTRAINT fk_role_unite_challenge_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_all_pkm_challenge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    pkm_id INT NOT NULL,
    is_played INT NOT NULL DEFAULT 0,
    win_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id, pkm_id),
    CONSTRAINT fk_role_all_pkm_challenge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_tower_defense` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    score INT NOT NULL DEFAULT 0,
    max_pass_game_level INT NOT NULL DEFAULT 0,
    is_new_unlock INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, period_id),
    CONSTRAINT fk_role_tower_defense_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_tower_defense_shop` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    shop_id INT NOT NULL,
    shop_buy_num INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, period_id, shop_id),
    CONSTRAINT fk_role_tower_defense_shop_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_tower_defense_buff` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    buff_id INT NOT NULL,
    buff_level INT NOT NULL,
    attr_id INT NOT NULL,
    is_new_unlock INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, period_id, buff_id),
    CONSTRAINT fk_role_tower_defense_buff_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_mega_evolution` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    complete INT NOT NULL DEFAULT 0,
    cur_stage INT NOT NULL DEFAULT 1,
    last_daily_update_time BIGINT NOT NULL DEFAULT 0,
    last_week_update_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, activity_id),
    CONSTRAINT fk_role_mega_evolution_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_mega_evolution_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    award_key INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id, award_key),
    CONSTRAINT fk_role_mega_evolution_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_mega_evolution_task` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    task_type INT NOT NULL,
    task_id INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id, task_type, task_id),
    CONSTRAINT fk_role_mega_evolution_task_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_eevee_challenge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    period INT NOT NULL,
    eevee_id INT NOT NULL DEFAULT 0,
    is_click INT NOT NULL DEFAULT 0,
    draw_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, activity_id, period),
    CONSTRAINT fk_role_eevee_challenge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_trainer_exp_gift` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    gift_type INT NOT NULL,
    reward_exp INT NOT NULL,
    award_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, gift_type, reward_exp),
    CONSTRAINT fk_role_trainer_exp_gift_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_star_road_site` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    site_id INT NOT NULL,
    cur_pokemon_id INT NOT NULL,
    end_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, site_id),
    UNIQUE KEY uq_role_star_road_site_pokemon (game_ulong_uid, cur_pokemon_id),
    CONSTRAINT fk_role_star_road_site_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_star_road` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    tips_site_id INT NOT NULL,
    pokemon_tips_close_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_star_road_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_achievement` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    achievement_id INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, achievement_id),
    CONSTRAINT fk_role_achievement_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_social` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    deny_all_request_flag INT NOT NULL DEFAULT 0,
    accept_chat_convention INT NOT NULL DEFAULT 0,
    last_logout_time BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT fk_role_social_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_friend` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    friend_uid BIGINT UNSIGNED NOT NULL,
    add_friend_time BIGINT NOT NULL,
    ban_chat_flag INT NOT NULL DEFAULT 0,
    intimacy_value INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, friend_uid),
    KEY ix_role_friend_friend (friend_uid),
    CONSTRAINT ck_role_friend_not_self CHECK (game_ulong_uid <> friend_uid),
    CONSTRAINT fk_role_friend_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_friend_friend FOREIGN KEY (friend_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_friend_request` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    request_uid BIGINT UNSIGNED NOT NULL,
    from_type INT NOT NULL,
    self_camp INT NOT NULL DEFAULT 0,
    request_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, request_uid),
    KEY ix_role_friend_request_requester (request_uid),
    CONSTRAINT ck_role_friend_request_not_self CHECK (game_ulong_uid <> request_uid),
    CONSTRAINT fk_role_friend_request_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_friend_request_requester FOREIGN KEY (request_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_friend_black` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    black_uid BIGINT UNSIGNED NOT NULL,
    add_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, black_uid),
    CONSTRAINT ck_role_friend_black_not_self CHECK (game_ulong_uid <> black_uid),
    CONSTRAINT fk_role_friend_black_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_friend_black_black FOREIGN KEY (black_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_daily_gift_coin` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    gift_day BIGINT NOT NULL,
    daily_gift_coin_uid BIGINT UNSIGNED NOT NULL,
    gift_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, gift_day, daily_gift_coin_uid),
    KEY ix_role_daily_gift_coin_friend (daily_gift_coin_uid),
    CONSTRAINT fk_role_daily_gift_coin_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_daily_gift_coin_friend FOREIGN KEY (daily_gift_coin_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_daily_gift_coin_recv` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    gift_day BIGINT NOT NULL,
    recv_count INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, gift_day),
    CONSTRAINT fk_role_daily_gift_coin_recv_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_chat_offline` (
    chat_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    dest_uid BIGINT UNSIGNED NOT NULL,
    from_uid BIGINT UNSIGNED NOT NULL,
    chat_time BIGINT NOT NULL,
    chat_msg VARBINARY(2048) NOT NULL,
    KEY ix_role_chat_offline_dest (dest_uid, chat_id),
    CONSTRAINT fk_role_chat_offline_dest FOREIGN KEY (dest_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_chat_offline_from FOREIGN KEY (from_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_mail` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    mail_index INT NOT NULL,
    mail_type INT NOT NULL,
    mail_sub_type INT NOT NULL,
    mail_id INT NOT NULL DEFAULT 0,
    mail_state INT NOT NULL,
    from_uid BIGINT UNSIGNED NOT NULL DEFAULT 0,
    from_name VARCHAR(64) NOT NULL DEFAULT '',
    subject VARCHAR(64) NOT NULL,
    content VARCHAR(400) NOT NULL,
    content_param VARBINARY(1024) NOT NULL DEFAULT '',
    send_time BIGINT NOT NULL,
    expire_time BIGINT NOT NULL,
    access_info VARBINARY(1024) NOT NULL,
    access_state INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, mail_index),
    KEY ix_role_mail_type (game_ulong_uid, mail_type),
    CONSTRAINT ck_role_mail_state CHECK (mail_state IN (1, 2)),
    CONSTRAINT ck_role_mail_access_state CHECK (access_state IN (0, 1, 2)),
    CONSTRAINT fk_role_mail_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
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

CREATE TABLE IF NOT EXISTS `role_setting` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    client_bit_state VARBINARY(64) NOT NULL,
    red_dot_switch_info VARBINARY(64) NOT NULL,
    lobby_menu_sort_info VARBINARY(128) NOT NULL,
    quick_speak_info VARBINARY(64) NULL,
    game_setting_info VARBINARY(4096) NOT NULL,
    ab_mode INT NOT NULL DEFAULT 1,
    country_code INT NOT NULL DEFAULT 0,
    red_dot_info VARBINARY(4096) NOT NULL,
    bub_tip_info VARBINARY(256) NOT NULL,
    is_close_intimacy_show INT NOT NULL DEFAULT 0,
    is_invisible INT NOT NULL DEFAULT 0,
    is_no_friend_invite_invisible INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT ck_role_setting_ab_mode CHECK (ab_mode IN (1, 2)),
    CONSTRAINT ck_role_setting_is_close_intimacy_show CHECK (is_close_intimacy_show IN (0, 1)),
    CONSTRAINT ck_role_setting_is_invisible CHECK (is_invisible IN (0, 1)),
    CONSTRAINT ck_role_setting_is_no_friend_invite_invisible CHECK (is_no_friend_invite_invisible IN (0, 1)),
    CONSTRAINT fk_role_setting_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
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
    last_battle_win_time BIGINT NOT NULL DEFAULT 0,
    statistic_info VARBINARY(64) NOT NULL DEFAULT '',
    current_season_id INT NOT NULL DEFAULT 0,
    current_season_statistic_info VARBINARY(64) NOT NULL DEFAULT '',
    current_week_start BIGINT NOT NULL DEFAULT 0,
    current_week_statistic_info VARBINARY(64) NOT NULL DEFAULT '',
    last_battle_time BIGINT NOT NULL DEFAULT 0,
    ranked_match_info_in_season VARBINARY(64) NOT NULL DEFAULT '',
    skill_recommend_info VARBINARY(64) NOT NULL DEFAULT '',
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_used_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_challenge_node` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    stage_id INT NOT NULL,
    node_id INT NOT NULL,
    difficulty_level INT NOT NULL,
    star_num INT NOT NULL,
    complete_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, stage_id, node_id, difficulty_level),
    CONSTRAINT fk_role_pve_challenge_node_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_challenge_week` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    week_start_time BIGINT NOT NULL,
    week_reward_get_num INT NOT NULL DEFAULT 0,
    privilege_reward_get_num INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_pve_challenge_week_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_challenge_buff` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    buff_id INT NOT NULL,
    buff_level INT NOT NULL,
    is_new_unlock TINYINT NOT NULL DEFAULT 1,
    PRIMARY KEY (game_ulong_uid, buff_id),
    CONSTRAINT fk_role_pve_challenge_buff_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_challenge_shop` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    shop_id INT NOT NULL,
    shop_buy_num INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, shop_id),
    CONSTRAINT fk_role_pve_challenge_shop_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pve_privilege` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    privilege_expire_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id),
    CONSTRAINT fk_role_pve_privilege_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_challenge_trainer_record` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    map_id INT NOT NULL,
    level_type INT NOT NULL,
    challenge_cnt INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, map_id, level_type),
    CONSTRAINT fk_role_challenge_trainer_record_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_newbie_challenge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    recv_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_newbie_challenge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_newbie_challenge_task` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id),
    CONSTRAINT fk_role_newbie_challenge_task_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_task_process` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    condition_index TINYINT NOT NULL DEFAULT 0,
    task_process INT NOT NULL,
    complete_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, task_id, period_start, condition_index),
    CONSTRAINT fk_role_task_process_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_task_process_value` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    condition_index TINYINT NOT NULL,
    counted_value INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id, period_start, condition_index, counted_value),
    CONSTRAINT fk_role_task_process_value_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_fight_history` (
    fight_history_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    map_sub_mode INT NOT NULL,
    fight_history_record MEDIUMBLOB NOT NULL,
    battle_review_info MEDIUMBLOB NOT NULL,
    record_time BIGINT NOT NULL,
    KEY ix_role_fight_history_mode (game_ulong_uid, map_sub_mode, fight_history_id),
    CONSTRAINT fk_role_fight_history_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_energy` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    energy INT NOT NULL,
    energy_free_capacity INT NOT NULL,
    next_flush_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_energy_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
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

CREATE TABLE IF NOT EXISTS `role_credit` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    credit_point INT NOT NULL,
    unconfirmed_day BIGINT NOT NULL DEFAULT 0,
    unconfirmed_count INT NOT NULL DEFAULT 0,
    ban_end_time BIGINT NOT NULL DEFAULT 0,
    resume_day BIGINT NOT NULL DEFAULT 0,
    resume_credit INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_role_credit_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_credit_reduce_record` (
    credit_reduce_record_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    game_start_time BIGINT NOT NULL,
    map_mode INT NOT NULL,
    map_sub_mode INT NOT NULL,
    reason INT NOT NULL,
    reduce_point INT NOT NULL,
    hero_id INT NOT NULL,
    desk_uniq_id VARCHAR(64) NOT NULL DEFAULT '',
    KEY ix_role_credit_reduce_record_role (game_ulong_uid, credit_reduce_record_id),
    KEY ix_role_credit_reduce_record_desk (game_ulong_uid, desk_uniq_id),
    CONSTRAINT fk_role_credit_reduce_record_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_accusation` (
    accusation_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    accused_uid BIGINT UNSIGNED NOT NULL,
    accused_account_id VARCHAR(64) NOT NULL DEFAULT '',
    accused_role_name VARCHAR(64) NOT NULL DEFAULT '',
    category INT NOT NULL,
    scene INT NOT NULL,
    reason VARCHAR(64) NOT NULL DEFAULT '',
    battle_id VARCHAR(64) NOT NULL DEFAULT '',
    battle_time INT NOT NULL DEFAULT 0,
    relay_id VARCHAR(32) NOT NULL DEFAULT '',
    description VARCHAR(1024) NOT NULL DEFAULT '',
    content MEDIUMTEXT NOT NULL,
    voice_accusation_source INT NOT NULL DEFAULT 0,
    accusation_result INT NOT NULL,
    credit_punish INT NOT NULL DEFAULT 0,
    accusation_time BIGINT NOT NULL,
    KEY ix_role_accusation_reporter (game_ulong_uid, accused_uid, battle_id),
    KEY ix_role_accusation_accused (accused_uid, accusation_id),
    CONSTRAINT fk_role_accusation_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ranked` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    protect_card_select_status INT NOT NULL DEFAULT 0,
    ban_rank_race_end_time BIGINT NOT NULL DEFAULT 0,
    no_reminded_buy_protect_card_day BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT ck_role_ranked_protect_card_select_status CHECK (protect_card_select_status IN (0, 1)),
    CONSTRAINT fk_role_ranked_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ranked_season` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    season_id INT NOT NULL,
    ranked_division INT NOT NULL,
    win_point INT NOT NULL DEFAULT 0,
    master_point INT NOT NULL DEFAULT 0,
    highest_ranked_division INT NOT NULL,
    highest_master_point INT NOT NULL DEFAULT 0,
    battle_cnt INT NOT NULL DEFAULT 0,
    win_cnt INT NOT NULL DEFAULT 0,
    entry_ranked_division INT NOT NULL DEFAULT 0,
    entry_win_point INT NOT NULL DEFAULT 0,
    entry_master_point INT NOT NULL DEFAULT 0,
    entry_ticket_consume INT NOT NULL DEFAULT 0,
    use_free_ticket TINYINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, season_id),
    CONSTRAINT ck_role_ranked_season_points CHECK (win_point >= 0 AND master_point >= 0 AND entry_win_point >= 0 AND entry_master_point >= 0),
    CONSTRAINT ck_role_ranked_season_counts CHECK (battle_cnt >= 0 AND win_cnt >= 0 AND win_cnt <= battle_cnt),
    CONSTRAINT ck_role_ranked_season_use_free_ticket CHECK (use_free_ticket IN (0, 1)),
    CONSTRAINT fk_role_ranked_season_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ranked_season_task_award` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    season_id INT NOT NULL,
    ranked_division INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, season_id, ranked_division),
    CONSTRAINT fk_role_ranked_season_task_award_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_warm_battle` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    map_sub_mode INT NOT NULL,
    game_cnt INT NOT NULL DEFAULT 0,
    single_warm_cnt INT NOT NULL DEFAULT 0,
    mult_warm_cnt INT NOT NULL DEFAULT 0,
    cont_lose_cnt INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, map_sub_mode),
    CONSTRAINT ck_role_warm_battle_counts CHECK (game_cnt >= 0 AND single_warm_cnt >= 0 AND mult_warm_cnt >= 0 AND single_warm_cnt + mult_warm_cnt <= game_cnt AND cont_lose_cnt >= 0 AND cont_lose_cnt <= game_cnt),
    CONSTRAINT fk_role_warm_battle_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_be_fabulous` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    be_fabulous_cnt INT NOT NULL DEFAULT 0,
    be_fbls_cnt_last_upd_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT ck_role_be_fabulous_cnt CHECK (be_fabulous_cnt >= 0),
    CONSTRAINT fk_role_be_fabulous_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_mic_tips` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    show_mic_tips_time BIGINT NOT NULL DEFAULT 0,
    show_mic_tips_cnt INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT ck_role_mic_tips_cnt CHECK (show_mic_tips_cnt >= 0),
    CONSTRAINT fk_role_mic_tips_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `guild` (
    guild_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    guild_name VARCHAR(64) NOT NULL,
    notice VARCHAR(512) NOT NULL DEFAULT '',
    join_type INT NOT NULL,
    label_1 INT NOT NULL,
    label_2 INT NOT NULL DEFAULT 0,
    label_3 INT NOT NULL DEFAULT 0,
    build_time BIGINT NOT NULL,
    PRIMARY KEY (guild_id),
    UNIQUE KEY ux_guild_name (guild_name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `guild_member` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    guild_id BIGINT UNSIGNED NOT NULL,
    office INT NOT NULL,
    join_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid),
    KEY ix_guild_member_guild (guild_id),
    CONSTRAINT fk_guild_member_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_guild_member_guild FOREIGN KEY (guild_id) REFERENCES `guild` (guild_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `guild_join_request` (
    guild_id BIGINT UNSIGNED NOT NULL,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    request_time BIGINT NOT NULL,
    PRIMARY KEY (guild_id, game_ulong_uid),
    KEY ix_guild_join_request_role (game_ulong_uid),
    CONSTRAINT fk_guild_join_request_guild FOREIGN KEY (guild_id) REFERENCES `guild` (guild_id) ON DELETE CASCADE,
    CONSTRAINT fk_guild_join_request_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `guild_comment` (
    guild_id BIGINT UNSIGNED NOT NULL,
    comment_index BIGINT NOT NULL,
    comment_type INT NOT NULL,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    member_name VARCHAR(64) NOT NULL,
    other_uid BIGINT UNSIGNED NOT NULL DEFAULT 0,
    other_name VARCHAR(64) NOT NULL DEFAULT '',
    content VARCHAR(256) NOT NULL DEFAULT '',
    text_id INT NOT NULL DEFAULT 0,
    comment_time BIGINT NOT NULL,
    PRIMARY KEY (guild_id, comment_index),
    CONSTRAINT fk_guild_comment_guild FOREIGN KEY (guild_id) REFERENCES `guild` (guild_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `guild_activity` (
    guild_id BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    reward_id INT NOT NULL DEFAULT 0,
    guild_score INT NOT NULL DEFAULT 0,
    PRIMARY KEY (guild_id, period_id),
    CONSTRAINT ck_guild_activity_score CHECK (guild_score >= 0),
    CONSTRAINT fk_guild_activity_guild FOREIGN KEY (guild_id) REFERENCES `guild` (guild_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_guild` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    last_quit_time BIGINT NOT NULL DEFAULT 0,
    quit_cnt INT NOT NULL DEFAULT 0,
    history_label_1 INT NOT NULL DEFAULT 0,
    history_label_2 INT NOT NULL DEFAULT 0,
    history_label_3 INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT ck_role_guild_quit_cnt CHECK (quit_cnt >= 0),
    CONSTRAINT fk_role_guild_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_guild_activity_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    period_id INT NOT NULL,
    reward_gear INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, period_id, reward_gear),
    CONSTRAINT fk_role_guild_activity_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_guild_activity_task` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    guild_id BIGINT UNSIGNED NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id, period_start),
    CONSTRAINT fk_role_guild_activity_task_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_badge_show` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    badge_show_list VARCHAR(64) NOT NULL DEFAULT '',
    CONSTRAINT fk_role_badge_show_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_tmp_name` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    tmp_name_type INT NOT NULL,
    tmp_name VARCHAR(64) NOT NULL,
    is_use INT NOT NULL DEFAULT 0,
    is_valid INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, tmp_name_type),
    CONSTRAINT fk_role_tmp_name_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_activation_code` (
    activation_code VARCHAR(32) NOT NULL PRIMARY KEY,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    get_time BIGINT NOT NULL,
    UNIQUE KEY uq_role_activation_code_role (game_ulong_uid),
    CONSTRAINT fk_role_activation_code_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `activation_code_use` (
    activation_code VARCHAR(32) NOT NULL,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    use_time BIGINT NOT NULL,
    PRIMARY KEY (activation_code, game_ulong_uid)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_battle_pass` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    season_id INT NOT NULL,
    level INT NOT NULL DEFAULT 1,
    exp INT NOT NULL DEFAULT 0,
    total_exp INT NOT NULL DEFAULT 0,
    pass_type INT NOT NULL DEFAULT 0,
    video_played INT NOT NULL DEFAULT 0,
    addition_treasure_cnt INT NOT NULL DEFAULT 0,
    addition_treasure_unget_cnt INT NOT NULL DEFAULT 0,
    addition_treasure_unget_exp INT NOT NULL DEFAULT 0,
    last_up_level_time BIGINT NOT NULL DEFAULT 0,
    last_gain_exp_time BIGINT NOT NULL DEFAULT 0,
    season_settled INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, season_id),
    CONSTRAINT ck_role_battle_pass_progress CHECK (level >= 1 AND exp >= 0 AND total_exp >= 0 AND addition_treasure_cnt >= 0 AND addition_treasure_unget_cnt >= 0 AND addition_treasure_unget_exp >= 0),
    CONSTRAINT fk_role_battle_pass_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_battle_pass_level_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    season_id INT NOT NULL,
    level INT NOT NULL,
    reward_type INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, season_id, level, reward_type),
    CONSTRAINT fk_role_battle_pass_level_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_battle_pass_task_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    task_id INT NOT NULL,
    period_start BIGINT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, task_id, period_start),
    CONSTRAINT fk_role_battle_pass_task_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_video_history` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    abs_id VARCHAR(64) NOT NULL,
    save_time BIGINT NOT NULL,
    collect_flag TINYINT NOT NULL DEFAULT 0,
    battle_version_section1 BIGINT UNSIGNED NOT NULL,
    battle_version_section2 BIGINT UNSIGNED NOT NULL,
    fight_history_record MEDIUMBLOB NOT NULL,
    PRIMARY KEY (game_ulong_uid, abs_id),
    CONSTRAINT ck_role_video_history_collect_flag CHECK (collect_flag IN (0, 1)),
    CONSTRAINT fk_role_video_history_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_video_history_tip` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    del_video_guide_tip TINYINT NOT NULL DEFAULT 0,
    video_del_by_version TINYINT NOT NULL DEFAULT 0,
    CONSTRAINT ck_role_video_history_tip_flags CHECK (del_video_guide_tip IN (0, 1) AND video_del_by_version IN (0, 1)),
    CONSTRAINT fk_role_video_history_tip_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_pokemon_carry_equipment` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_id INT NOT NULL,
    carry_equipment_plan_id INT NOT NULL DEFAULT 0,
    equipment_id_list VARCHAR(64) NOT NULL,
    PRIMARY KEY (game_ulong_uid, pokemon_id),
    CONSTRAINT fk_role_pokemon_carry_equipment_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_paster_time_limited` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    paster_id INT NOT NULL,
    gain_time BIGINT NOT NULL DEFAULT 0,
    new_tag INT NOT NULL DEFAULT 0,
    time_limited BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, paster_id),
    CONSTRAINT fk_role_paster_time_limited_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_paster_spray` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    paster_id INT NOT NULL,
    paster_spray_id INT NOT NULL,
    time_limited BIGINT NOT NULL,
    is_wearing TINYINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, paster_id, paster_spray_id),
    CONSTRAINT fk_role_paster_spray_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_skill_effects_expand_pack` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    pokemon_avatar_id INT NOT NULL,
    skill_effects_expand_pack_type INT NOT NULL,
    skill_effects_expand_pack_id INT NOT NULL,
    PRIMARY KEY (game_ulong_uid, pokemon_avatar_id, skill_effects_expand_pack_type),
    CONSTRAINT fk_role_skill_effects_expand_pack_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ten_games_team` (
    team_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    team_name VARCHAR(64) NOT NULL,
    team_leader_uid BIGINT UNSIGNED NOT NULL,
    join_ten_games_id INT NOT NULL DEFAULT 0,
    cur_score INT NOT NULL DEFAULT 0,
    win_count INT NOT NULL DEFAULT 0,
    lose_count INT NOT NULL DEFAULT 0,
    modify_name_time BIGINT NOT NULL DEFAULT 0,
    create_time BIGINT NOT NULL,
    PRIMARY KEY (team_id),
    UNIQUE KEY ux_ten_games_team_name (team_name),
    KEY ix_ten_games_team_games (join_ten_games_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ten_games_team_member` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    team_id BIGINT UNSIGNED NOT NULL,
    join_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid),
    KEY ix_ten_games_team_member_team (team_id),
    CONSTRAINT fk_ten_games_team_member_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_ten_games_team_member_team FOREIGN KEY (team_id) REFERENCES `ten_games_team` (team_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ten_games_team_invite` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    team_id BIGINT UNSIGNED NOT NULL,
    invite_uid BIGINT UNSIGNED NOT NULL,
    invited_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, team_id),
    KEY ix_ten_games_team_invite_team (team_id),
    CONSTRAINT fk_ten_games_team_invite_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_ten_games_team_invite_team FOREIGN KEY (team_id) REFERENCES `ten_games_team` (team_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ten_games` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    ten_games_team_create_time BIGINT NOT NULL DEFAULT 0,
    ten_games_last_read_invite_time BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_ten_games_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ten_games_history` (
    games_id INT NOT NULL,
    team_id BIGINT UNSIGNED NOT NULL,
    team_name VARCHAR(64) NOT NULL,
    team_leader_uid BIGINT UNSIGNED NOT NULL,
    total_score INT NOT NULL,
    rank_no INT NOT NULL,
    PRIMARY KEY (games_id, team_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ten_games_history_member` (
    games_id INT NOT NULL,
    team_id BIGINT UNSIGNED NOT NULL,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (games_id, team_id, game_ulong_uid),
    KEY ix_ten_games_history_member_role (game_ulong_uid),
    CONSTRAINT fk_ten_games_history_member_history FOREIGN KEY (games_id, team_id) REFERENCES `ten_games_history` (games_id, team_id) ON DELETE CASCADE,
    CONSTRAINT fk_ten_games_history_member_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ten_games_bp` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    is_buy_senier_reward TINYINT NOT NULL DEFAULT 0,
    win_cnt INT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid, activity_id),
    CONSTRAINT fk_role_ten_games_bp_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_ten_games_bp_reward` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    reward_seq INT NOT NULL,
    receive_reward_type INT NOT NULL,
    reward_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id, reward_seq, receive_reward_type),
    CONSTRAINT fk_role_ten_games_bp_reward_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_camp_battle` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    activity_id INT NOT NULL,
    camp INT NOT NULL,
    score BIGINT NOT NULL DEFAULT 0,
    is_award TINYINT NOT NULL DEFAULT 0,
    join_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, activity_id),
    KEY ix_role_camp_battle_activity (activity_id, camp),
    CONSTRAINT fk_role_camp_battle_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_first_charge` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    first_charge_time BIGINT NOT NULL DEFAULT 0,
    last_face_shot_time BIGINT NOT NULL DEFAULT 0,
    face_shot_cnt INT NOT NULL DEFAULT 0,
    is_ban_face_shot TINYINT NOT NULL DEFAULT 0,
    PRIMARY KEY (game_ulong_uid),
    CONSTRAINT fk_role_first_charge_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_first_charge_award` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    login_seq INT NOT NULL,
    award_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid, login_seq),
    CONSTRAINT fk_role_first_charge_award_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_coupons_flow` (
    flow_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    num INT NOT NULL,
    flow_time BIGINT NOT NULL,
    system_type INT NOT NULL,
    extra_info BLOB NOT NULL,
    PRIMARY KEY (flow_id),
    KEY ix_role_coupons_flow_role (game_ulong_uid, flow_id),
    CONSTRAINT fk_role_coupons_flow_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `role_invite_friend` (
    game_ulong_uid BIGINT UNSIGNED NOT NULL,
    friend_ulong_uid BIGINT UNSIGNED NOT NULL,
    bind_time BIGINT NOT NULL,
    PRIMARY KEY (game_ulong_uid),
    KEY ix_role_invite_friend_friend (friend_ulong_uid),
    CONSTRAINT ck_role_invite_friend_not_self CHECK (friend_ulong_uid <> game_ulong_uid),
    CONSTRAINT fk_role_invite_friend_role FOREIGN KEY (game_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE,
    CONSTRAINT fk_role_invite_friend_friend FOREIGN KEY (friend_ulong_uid) REFERENCES `role` (game_ulong_uid) ON DELETE CASCADE
) ENGINE=InnoDB;

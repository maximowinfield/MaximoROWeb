CREATE TABLE IF NOT EXISTS `web_email_identity`
(
    `account_id` INT UNSIGNED NOT NULL,
    `email` VARCHAR(39) NOT NULL,
    `created_at_utc` DATETIME(6) NOT NULL,
    `verified_at_utc` DATETIME(6) NULL,
    PRIMARY KEY (`account_id`),
    UNIQUE KEY `uq_web_email_identity_email` (`email`)
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `web_email_verification_token`
(
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    `account_id` INT UNSIGNED NOT NULL,
    `token_hash` BINARY(32) NOT NULL,
    `created_at_utc` DATETIME(6) NOT NULL,
    `expires_at_utc` DATETIME(6) NOT NULL,
    `consumed_at_utc` DATETIME(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_web_email_token_hash` (`token_hash`),
    KEY `ix_web_email_token_account` (`account_id`),
    KEY `ix_web_email_token_expiry` (`expires_at_utc`)
)
ENGINE=InnoDB
DEFAULT CHARSET=utf8mb4
COLLATE=utf8mb4_unicode_ci;

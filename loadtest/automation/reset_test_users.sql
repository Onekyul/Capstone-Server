-- ============================================================
-- reset_test_users.sql
-- Reset test user data before each paper measurement
--
-- Run before every measurement to guarantee identical start state.
-- Satisfies reproducibility requirements stated in paper Chapter 3.
--
-- Target : userId 1001~1200 (measurement-only users)
-- Kept   : users.id, device_id, nickname (user accounts preserved)
-- Deleted: user_items, user_equipments, user_enchants (game progress data)
-- Reset  : users.max_cleared_stage, equipped_*_id (progress columns)
-- ============================================================

-- Delete game progress data for test users
DELETE FROM user_items       WHERE user_id BETWEEN 1001 AND 1200;
DELETE FROM user_equipments  WHERE user_id BETWEEN 1001 AND 1200;
DELETE FROM user_enchants    WHERE user_id BETWEEN 1001 AND 1200;

-- Reset only progress-related columns in users table
-- (device_id, nickname, id are preserved)
UPDATE users
SET max_cleared_stage  = 0,
    equipped_weapon_id = NULL,
    equipped_helmet_id = NULL,
    equipped_armor_id  = NULL,
    equipped_boots_id  = NULL
WHERE id BETWEEN 1001 AND 1200;

-- Verification is performed by run_single_measurement.ps1 after this script runs.

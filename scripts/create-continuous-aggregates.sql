-- TimescaleDB Continuous Aggregates for Sensor Data
-- This script creates continuous aggregates for 10-minute and 1-hour aggregations

-- Ensure TimescaleDB extension is enabled
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Convert telemetry_events table to hypertable if not already done
-- Note: This should already be done during initial migration, but checking here
-- If table has data, use migrate_data => true
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM timescaledb_information.hypertables 
        WHERE hypertable_name = 'telemetry_events'
    ) THEN
        -- Check if table has data
        IF EXISTS (SELECT 1 FROM telemetry_events LIMIT 1) THEN
            PERFORM create_hypertable('telemetry_events', 'timestamp', migrate_data => TRUE);
        ELSE
            PERFORM create_hypertable('telemetry_events', 'timestamp', if_not_exists => TRUE);
        END IF;
    END IF;
END $$;

-- Continuous Aggregate: 10-minute aggregations for Sensor Readings
-- This provides the latest 10-minute aggregated values for each sensor
CREATE MATERIALIZED VIEW IF NOT EXISTS sensor_readings_10min
WITH (timescaledb.continuous) AS
SELECT 
    time_bucket('10 minutes', timestamp) AS bucket,
    factory_id,
    tag,
    equipment_type,
    equipment_name,
    source_id,
    AVG((value_json::jsonb->>'value')::numeric) AS avg_value,
    MIN((value_json::jsonb->>'value')::numeric) AS min_value,
    MAX((value_json::jsonb->>'value')::numeric) AS max_value,
    COUNT(*) AS count,
    MAX(timestamp) AS last_timestamp
FROM telemetry_events
WHERE quality = 0  -- Only Good quality data
GROUP BY bucket, factory_id, tag, equipment_type, equipment_name, source_id
WITH NO DATA;

-- Add refresh policy for 10-minute aggregate (refresh every 5 minutes)
SELECT add_continuous_aggregate_policy('sensor_readings_10min',
    start_offset => INTERVAL '3 hours',
    end_offset => INTERVAL '10 minutes',
    schedule_interval => INTERVAL '5 minutes',
    if_not_exists => TRUE);

-- Continuous Aggregate: 1-hour aggregations for Sensor Trends (24h)
-- This provides hourly aggregated values for trend charts
CREATE MATERIALIZED VIEW IF NOT EXISTS sensor_trends_1hour
WITH (timescaledb.continuous) AS
SELECT 
    time_bucket('1 hour', timestamp) AS bucket,
    factory_id,
    tag,
    equipment_type,
    equipment_name,
    source_id,
    AVG((value_json::jsonb->>'value')::numeric) AS avg_value,
    MIN((value_json::jsonb->>'value')::numeric) AS min_value,
    MAX((value_json::jsonb->>'value')::numeric) AS max_value,
    COUNT(*) AS count
FROM telemetry_events
WHERE quality = 0  -- Only Good quality data
GROUP BY bucket, factory_id, tag, equipment_type, equipment_name, source_id
WITH NO DATA;

-- Add refresh policy for 1-hour aggregate (refresh every 15 minutes)
SELECT add_continuous_aggregate_policy('sensor_trends_1hour',
    start_offset => INTERVAL '25 hours',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '15 minutes',
    if_not_exists => TRUE);

-- Create indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_factory_tag 
    ON sensor_readings_10min (factory_id, tag, bucket DESC);

CREATE INDEX IF NOT EXISTS idx_sensor_readings_10min_last_timestamp 
    ON sensor_readings_10min (last_timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_sensor_trends_1hour_factory_tag 
    ON sensor_trends_1hour (factory_id, tag, bucket DESC);

-- Initial data refresh (optional - can be done manually or wait for automatic refresh)
-- REFRESH MATERIALIZED VIEW sensor_readings_10min;
-- REFRESH MATERIALIZED VIEW sensor_trends_1hour;



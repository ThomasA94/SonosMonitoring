namespace SonosMonitoring.Infrastructure.History;

/// <summary>Provides the explicit first-install schema for the separate observation journal.</summary>
public static class ObservationHistorySchema
{
    /// <summary>Returns a transactional PostgreSQL creation script that fails if the schema already exists.</summary>
    /// <returns>Schema version 1 creation SQL.</returns>
    public static string GetPostgresScript() => """
        BEGIN;
        CREATE SCHEMA sonos_history;
        CREATE TABLE sonos_history.schema_version
        (
            singleton boolean PRIMARY KEY CHECK (singleton),
            version integer NOT NULL
        );
        INSERT INTO sonos_history.schema_version (singleton, version) VALUES (true, 1);

        CREATE TABLE sonos_history.entries
        (
            sequence bigserial PRIMARY KEY,
            kind text NOT NULL CHECK (kind IN ('baseline', 'change', 'gap')),
            recorded_at timestamptz NOT NULL,
            observed_at timestamptz,
            device_id varchar(128),
            before_state jsonb,
            after_state jsonb,
            reason varchar(64),
            CONSTRAINT ck_sonos_history_entry_shape CHECK
            (
                (kind = 'gap' AND device_id IS NULL AND before_state IS NULL AND after_state IS NULL AND reason IS NOT NULL)
                OR
                (kind = 'baseline' AND device_id IS NOT NULL AND before_state IS NULL AND after_state IS NOT NULL AND reason IS NULL)
                OR
                (kind = 'change' AND device_id IS NOT NULL AND before_state IS NOT NULL AND after_state IS NOT NULL AND reason IS NULL)
            )
        );
        CREATE INDEX ix_sonos_history_entries_recorded_at ON sonos_history.entries (recorded_at);
        CREATE INDEX ix_sonos_history_entries_device_sequence ON sonos_history.entries (device_id, sequence);

        CREATE TABLE sonos_history.checkpoints
        (
            device_id varchar(128) PRIMARY KEY,
            state jsonb NOT NULL,
            last_seen timestamptz NOT NULL
        );

        CREATE TABLE sonos_history.meta
        (
            singleton boolean PRIMARY KEY CHECK (singleton),
            run_id uuid,
            last_recorded timestamptz,
            collector_observing boolean
        );
        INSERT INTO sonos_history.meta (singleton, run_id, last_recorded, collector_observing)
        VALUES (true, NULL, NULL, NULL);
        COMMIT;
        """;
}

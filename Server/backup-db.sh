#!/bin/sh
# Run on the deployment host from Server/, before applying schema changes.
# Keeps the existing database and volumes untouched. Copy backups to separate protected storage.
set -eu
set -C
umask 077
backup_dir="${1:?Usage: sh backup-db.sh /absolute/protected/backup-directory}"
case "$backup_dir" in /*) ;; *) echo "Use an absolute backup directory." >&2; exit 1 ;; esac
mkdir -p "$backup_dir"
backup_file="$backup_dir/lexiflow-$(date -u +%Y%m%dT%H%M%SZ).dump"
if [ -e "$backup_file" ]; then echo "Backup already exists; refusing to overwrite." >&2; exit 1; fi
docker compose exec -T db pg_dump -U worddb -d worddb --format=custom --no-owner --no-acl > "$backup_file"
test -s "$backup_file"
docker compose exec -T db pg_restore --list < "$backup_file" > /dev/null
echo "Backup created and archive index checked: $backup_file"
echo "A restore rehearsal into a separate disposable database is still required."

#!/usr/bin/env python3
"""Verify an OFF-HOST decrypted bundle on an isolated disposable database only."""
import hashlib
import json
import os
from pathlib import Path
import tarfile
import tempfile
import backup


def main():
    import fcntl
    os.umask(0o077)
    source = backup.ROOT / 'recovery-check.tar.gz'
    if source.is_symlink() or source.stat().st_size >= backup.MAX_BYTES:
        raise ValueError('Unexpected recovery archive')
    with (backup.ROOT / '.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with tempfile.TemporaryDirectory(prefix='.recovery-', dir=backup.ROOT) as temporary:
            directory = Path(temporary)
            with tarfile.open(source, 'r:gz') as archive:
                members = archive.getmembers()
                if {m.name for m in members} != {'worddb.dump', 'manifest.json'} or len(members) != 2:
                    raise ValueError('Unexpected recovery bundle contents')
                for member in members:
                    if not member.isfile() or member.size > backup.MAX_BYTES:
                        raise ValueError('Unsafe recovery member')
                    with archive.extractfile(member) as stream, (directory / member.name).open('xb') as output:
                        data = stream.read(backup.MAX_BYTES + 1)
                        if len(data) > backup.MAX_BYTES:
                            raise ValueError('Oversized recovery member')
                        output.write(data)
            manifest = json.loads((directory / 'manifest.json').read_text())
            if backup.sha(directory / 'worddb.dump') != manifest['dump_sha256']:
                raise ValueError('Recovered dump checksum mismatch')
            counts = manifest['counts']
            backup.restore_check(directory / 'worddb.dump', list(counts), counts)
            state = json.loads((backup.ROOT / 'status.json').read_text())
            state['recovery_key_verified_at'] = backup.stamp(backup.now())
            state['recovery_certificate_sha256'] = manifest['recovery_certificate_sha256']
            backup.write_json(backup.ROOT / 'status.json', state)
            print(json.dumps({'off_host_decryption_and_restore': True, 'counts': counts}, indent=2))
        source.unlink()


if __name__ == '__main__':
    main()

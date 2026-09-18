#!/usr/bin/env python3
"""Publish one operational boolean; never upload database contents or credentials."""
import json
import os
import subprocess
import sys

import backup

COMPARTMENT = 'ocid1.tenancy.oc1..aaaaaaaalzkpfq6v3m4itqqot6isgy3xj5yurijsgy5o5epodxlbvcocldva'
ENDPOINT = 'https://telemetry-ingestion.ap-tokyo-1.oraclecloud.com'


def collect_health():
    try:
        config = backup.load_config()
        state = json.loads((backup.ROOT / 'status.json').read_text())
        issues = backup.health_issues(state, config, backup.now())
        success = state.get('last_success')
        if success:
            backup.verify_cloud(config, success['file'], success['sha256'])
        timer = subprocess.run(['systemctl', 'is-active', '--quiet', 'lexiflow-backup.timer'],
                               timeout=15, capture_output=True)
        if timer.returncode:
            issues.append('BACKUP_TIMER_INACTIVE')
        service = subprocess.run(['systemctl', 'show', 'lexiflow-backup.service',
                                  '--property=Result', '--value'],
                                 timeout=15, capture_output=True, text=True, check=True)
        if service.stdout.strip() != 'success':
            issues.append('BACKUP_SERVICE_FAILED')
        return issues
    except Exception as error:
        # Error text may contain a secret PAR URL. Only a type label is safe.
        return ['HEALTH_CHECK_FAILED_' + type(error).__name__]


def publish(healthy):
    import oci
    signer = oci.auth.signers.InstancePrincipalsSecurityTokenSigner()
    client = oci.monitoring.MonitoringClient(
        {}, signer=signer, service_endpoint=ENDPOINT, timeout=(10, 30),
        retry_strategy=oci.retry.NoneRetryStrategy())
    point = oci.monitoring.models.Datapoint(timestamp=backup.now(), value=int(healthy))
    metric = oci.monitoring.models.MetricDataDetails(
        namespace='lexiflow_backup', compartment_id=COMPARTMENT,
        name='BackupHealthy', dimensions={'application': 'lexiflow'},
        datapoints=[point])
    response = client.post_metric_data(oci.monitoring.models.PostMetricDataDetails(
        metric_data=[metric], batch_atomicity='ATOMIC'))
    if response.data.failed_metrics_count:
        raise RuntimeError('Metric publication rejected')


def main():
    os.umask(0o077)
    issues = collect_health()
    try:
        publish(not issues)
        result = {'checked_at': backup.stamp(backup.now()), 'published': True,
                  'healthy': not issues, 'issues': issues}
        backup.write_json(backup.ROOT / 'monitor-status.json', result)
        print(json.dumps(result))
        return 0
    except Exception as error:
        print('Metric publication failed: ' + type(error).__name__, file=sys.stderr)
        return 1  # The independent OCI absence alarm catches silent publishers.


if __name__ == '__main__':
    sys.exit(main())

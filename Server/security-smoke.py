#!/usr/bin/env python3
"""Exercise only newly-created disposable accounts; never use existing credentials.

Default: verify production HTTPS. --rehearsal targets a loopback-only test container.
"""
import json
import secrets
import sys
import urllib.error
import urllib.request

rehearsal = sys.argv[1:] == ['--rehearsal']
if sys.argv[1:] and not rehearsal:
    raise SystemExit('Only --rehearsal is supported.')
base = 'http://127.0.0.1:15276' if rehearsal else 'https://lexiflow.duckdns.org'
checks = 0

def call(path, expected, method='GET', body=None, token=None):
    global checks
    headers = {'Content-Type': 'application/json'}
    if rehearsal:
        headers['X-Forwarded-Proto'] = 'https'
    if token:
        headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(),
                                     headers=headers, method=method)
    try:
        response = urllib.request.urlopen(request, timeout=20)
    except urllib.error.HTTPError as response_error:
        response = response_error
    with response:
        status = response.status
        payload = response.read()
    if status != expected:
        raise RuntimeError(f'{method} {path}: expected {expected}, received {status}')
    checks += 1
    print(f'PASS {method} {path}: {status}', flush=True)
    return json.loads(payload) if payload and status < 300 else None

call('/health', 200)
call('/users/me', 401)
call('/words', 200)
suffix = secrets.token_hex(6)
password = secrets.token_urlsafe(24)
new_password = secrets.token_urlsafe(24)
accounts = []
try:
    for letter in ['a', 'b']:
        username = 'security_check_' + suffix + '_' + letter
        account = call('/users', 201, 'POST', {'UserId': username, 'Pw': password})
        account.update(password=password, token=None)
        accounts.append(account)
        login = call('/users/login', 200, 'POST', {'UserId': username, 'Pw': password})
        account['token'] = login['accessToken']
    alice, bob = accounts
    call('/users/me', 200, token=alice['token'])
    call('/users/' + str(bob['id']), 403, token=alice['token'])
    for kind, field in [('progress', 'WordId'), ('grammar-progress', 'GrammarId'), ('idiom-progress', 'IdiomId')]:
        call('/users/' + bob['userId'] + '/' + kind, 403, token=alice['token'])
        call('/users/' + bob['userId'] + '/' + kind, 403, 'POST', {field: 'test-item', 'Correct': True}, alice['token'])
    call('/admin/api/words', 401, token=alice['token'])
    call('/users/' + str(alice['id']), 204, 'PATCH',
         {'CurrentPw': password, 'Pw': new_password}, alice['token'])
    alice['password'] = new_password
    call('/users/me', 401, token=alice['token'])
    login = call('/users/login', 200, 'POST', {'UserId': alice['userId'], 'Pw': new_password})
    alice['token'] = login['accessToken']
finally:
    for account in accounts:
        if account.get('token'):
            call('/users/' + str(account['id']), 204, 'DELETE',
                 {'CurrentPw': account['password']}, account['token'])
            call('/users/me', 401, token=account['token'])
print(f'{checks} deployment checks passed; disposable accounts removed.')

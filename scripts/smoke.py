#!/usr/bin/env python3
"""Integration checks against a RUNNING practice API; no third-party packages."""
import concurrent.futures
import json
import sys
import urllib.request
import urllib.error
import uuid

base = (sys.argv[1] if len(sys.argv) > 1 else 'http://localhost:5000').rstrip('/')

def post(key, amount=125.50):
    body = json.dumps({'merchantReference': 'merchant-lab-1', 'amount': amount, 'currency': 'INR'}).encode()
    request = urllib.request.Request(base + '/api/payment-requests', data=body,
        headers={'Content-Type': 'application/json', 'Idempotency-Key': key}, method='POST')
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)

key = 'smoke-' + str(uuid.uuid4())
status, first = post(key)
assert status == 201, (status, first)
status, replay = post(key)
assert status == 200 and replay['id'] == first['id'], (status, replay)
status, conflict = post(key, 126.50)
assert status == 409, (status, conflict)
status, invalid = post('bad-' + str(uuid.uuid4()), -1)
assert status == 400, (status, invalid)
parallel_key = 'parallel-' + str(uuid.uuid4())
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
    results = list(pool.map(lambda _: post(parallel_key), range(10)))
assert all(status in (200, 201) for status, body in results), results
assert sum(status == 201 for status, body in results) == 1, results
assert len({body['id'] for status, body in results}) == 1, results
print('PASS: create, replay, mismatch, invalid amount, and ten concurrent same-key requests.')
print('Save this ID for the persistence drill:', first['id'])

#!/usr/bin/env python3
"""End-to-end API assertion, with no dependencies. Run after starting API."""
import json
import os
import urllib.request

base = os.environ.get('BILLING_URL', 'http://127.0.0.1:5080')
def request(path, data=None):
    req = urllib.request.Request(base + path, data=None if data is None else json.dumps(data).encode(), headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(req, timeout=15) as response:
        text = response.read().decode()
        return json.loads(text) if text else None
request('/api/demo/seed', {})
request('/api/invoices/2026-11', {})
batch = request('/api/batches', {'period':'2026-11','effectiveDate':'2026-11-06'})
assert batch['id'] == request('/api/batches', {'period':'2026-11','effectiveDate':'2026-11-06'})['id']
with urllib.request.urlopen(base + '/api/batches/' + batch['id'] + '/ach') as r:
    ach = r.read().decode()
assert all(len(line)==94 for line in ach.splitlines())
results = [{'paymentId':p['id'],'outcome':'Settled' if i==0 else 'Returned','returnCode':None if i==0 else 'R01'} for i,p in enumerate(batch['payments'])]
request('/api/batches/'+batch['id']+'/reconcile', results)
request('/api/batches/'+batch['id']+'/reconcile', results)
report = request('/api/reports')
assert report['invoicedCents']==37800 and report['settledCents']+report['returnedCents']==37800 and report['pendingCents']==0, report
os.makedirs('artifacts', exist_ok=True)
with open('artifacts/fictional-demo.ach','w') as f: f.write(ach)
print(json.dumps({'batch':batch['id'],'report':report,'ach':'artifacts/fictional-demo.ach'},indent=2))

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNet } from '../wwwroot/net/client.js';

test('alpha is 0 before any snapshot', () => {
  const net = createNet({});
  assert.equal(net.alpha(123), 0);
});

test('ingest decodes entities and rotates prev<-cur', () => {
  const net = createNet({});
  // wire: [id, x, z, activity, (unused), yaw, kind, groundY]
  net.ingest({ entities: [[7, 10, 20, 2, 99, 90, 1, 5]] }, 100);
  assert.equal(net.curSnap.size, 1);
  assert.deepEqual(net.curSnap.get(7), [10, 20, 90, 5, 1, 2]); // [x,z,yaw,groundY,kind,activity]
  assert.equal(net.prevSnap.size, 0);
  net.ingest({ entities: [[7, 11, 21, 2, 99, 90, 1, 5]] }, 100.2);
  assert.equal(net.prevSnap.get(7)[0], 10); // previous cur became prev
  assert.equal(net.curSnap.get(7)[0], 11);
});

test('alpha uses the snapshot gap and clamps to [0,1]', () => {
  const net = createNet({});
  net.ingest({ entities: [] }, 100);
  net.ingest({ entities: [] }, 100.2); // gap = 0.2
  assert.equal(net.alpha(100.2), 0);   // at curTime
  // Note: 0.5 assertion allows floating-point tolerance due to binary representation of decimals
  assert.ok(Math.abs(net.alpha(100.3) - 0.5) < 1e-10, 'alpha(100.3) should be approximately 0.5');
  assert.equal(net.alpha(101), 1);     // clamped
});

test('onSnapshot/onDetail/onBuilding are invoked by ingest path only via connect; ingest stays pure', () => {
  let called = 0;
  const net = createNet({ onSnapshot: () => { called++; } });
  net.ingest({ entities: [] }, 1); // ingest itself must NOT fire onSnapshot
  assert.equal(called, 0);
});

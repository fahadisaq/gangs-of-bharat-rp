'use strict';

// Lets `node --test admin/test` run every test file in this folder
// (Node resolves the folder to this index.js).

require('./policy.test');
require('./server.test');
require('./firebase-rules.test');

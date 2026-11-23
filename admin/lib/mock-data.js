'use strict';

// In-memory mock data for the admin panel. Everything here is invented test data.
// A real deployment would read from Firestore and the game server instead.

function createMockData() {
  return {
    // Admin accounts. "name" and "accountId" are internal and never sent by the API.
    admins: [
      { code: 'ADM-01', level: 6, name: 'Mock Owner', accountId: 'acc-owner' },
      { code: 'ADM-05', level: 5, name: 'Mock Head', accountId: 'acc-head' },
      { code: 'ADM-12', level: 4, name: 'Mock Senior', accountId: 'acc-senior' },
      { code: 'ADM-27', level: 3, name: 'Mock Admin', accountId: 'acc-admin' },
      { code: 'ADM-33', level: 2, name: 'Mock Moderator', accountId: 'acc-mod' },
      { code: 'ADM-48', level: 1, name: 'Mock Helper', accountId: 'acc-helper' },
    ],
    players: [
      {
        id: 'ch-1001',
        character: 'Raju Shinde',
        faction: 'police',
        rank: 3,
        district: 'Old City',
        pingMs: 48,
        cashRupees: 12500,
        bankRupees: 80000,
        realInfo: { email: 'player1001@example.invalid', phone: '+00 0000001001', paymentsRupees: 499 },
      },
      {
        id: 'ch-1002',
        character: 'Meera Kapoor',
        faction: 'media',
        rank: 1,
        district: 'Film City',
        pingMs: 62,
        cashRupees: 4300,
        bankRupees: 150000,
        realInfo: { email: 'player1002@example.invalid', phone: '+00 0000001002', paymentsRupees: 0 },
      },
      {
        id: 'ch-1003',
        character: 'Salim Bhai',
        faction: 'gang_port',
        rank: 0,
        district: 'Port',
        pingMs: 95,
        cashRupees: 70000,
        bankRupees: 5000,
        realInfo: { email: 'player1003@example.invalid', phone: '+00 0000001003', paymentsRupees: 1299 },
      },
      {
        id: 'ch-1004',
        character: 'Anita Rao',
        faction: 'civilian',
        rank: null,
        district: 'Market',
        pingMs: 40,
        cashRupees: 900,
        bankRupees: 2000,
        realInfo: { email: 'player1004@example.invalid', phone: '+00 0000001004', paymentsRupees: 0 },
      },
    ],
    tickets: [
      { id: 'T-501', playerId: 'ch-1004', category: 'bug', message: 'Car stuck in wall near market', status: 'open', assignedAdminCode: null },
      { id: 'T-502', playerId: 'ch-1001', category: 'report', message: 'Player spamming ads', status: 'assigned', assignedAdminCode: 'ADM-48' },
    ],
    bans: [
      { playerId: 'ch-0999', kind: 'permanent', reason: 'Speed hack detected and confirmed', adminCode: 'ADM-05', appealUsed: true },
    ],
    economy: {
      // Money supply per day (₹), for the chart.
      days: ['D-6', 'D-5', 'D-4', 'D-3', 'D-2', 'D-1', 'Today'],
      moneySupplyRupees: [41000000, 41800000, 42500000, 42100000, 43000000, 43900000, 44200000],
      sourcesRupees: { salaries: 1800000, jobs: 950000, events: 300000 },
      sinksRupees: { taxes: 700000, fuel: 420000, ads: 160000, fines: 230000, repairs: 380000 },
      suspiciousTransfers: [{ from: 'ch-1003', to: 'ch-1004', amountRupees: 500000, note: 'Large transfer to a new player' }],
    },
    store: [
      { id: 'vip_30d', name: 'VIP 30 days', coins: 500, active: true },
      { id: 'skin_monsoon', name: 'Monsoon jacket (limited)', coins: 250, active: false },
    ],
    factions: [
      { key: 'police', name: 'Police', headCharacterId: 'ch-2001', termEndsAt: '2025-03-01' },
      { key: 'media', name: 'Mass Media', headCharacterId: 'ch-2006', termEndsAt: '2025-03-01' },
      { key: 'gang_port', name: 'Port Gang', headCharacterId: 'ch-1003', termEndsAt: null },
    ],
    // Faction permission changes and treasury spends from faction_audit.
    factionAudit: [
      { at: '2025-01-10T10:00:00Z', factionKey: 'police', action: 'grant_power', actorId: 'ch-2001', targetId: 'ch-1001', detail: 'recruit' },
      { at: '2025-01-10T11:30:00Z', factionKey: 'media', action: 'set_limit', actorId: 'ch-2006', targetId: 'ch-1002', detail: 'spend_treasury 10000/day' },
      { at: '2025-01-11T09:15:00Z', factionKey: 'media', action: 'grant_power', actorId: 'ch-2006', targetId: 'ch-1002', detail: 'go_live' },
      { at: '2025-01-11T18:40:00Z', factionKey: 'police', action: 'promote', actorId: 'ch-2001', targetId: 'ch-1001', detail: 'rank 4 -> 3' },
    ],
    events: [
      { id: 'E-1', name: 'Gold transport', schedule: 'daily 20:00 IST' },
      { id: 'E-2', name: 'VIP escort', schedule: 'weekly Sat 21:00 IST' },
    ],
    adminLog: [],
  };
}

module.exports = { createMockData };

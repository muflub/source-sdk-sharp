/*
 * Groups that ship with the tool.
 *
 * They exist to make the feature usable the moment it is opened, and to be worth
 * something in their own right: each is a small thing that is tedious to build by
 * hand and useful to drop into a map while testing.
 *
 * Every class used here is one this mod implements in C# — see the Scripted tab —
 * and every one of them is a POINT entity. That is deliberate: a brush entity's
 * volume lives in the compiled map, so a trigger_multiple created at runtime
 * would arrive with no brush and never fire. A template that cannot work when
 * placed would be worse than no template at all.
 *
 * Positions are relative to (0, 0, 0). Placing a group at an aim point moves the
 * whole thing so its footprint centre lands there, so these read as offsets from
 * wherever it is dropped.
 */

import type { Connection, Vec3 } from '../types';

export interface GroupEntity {
  classname: string;
  targetname: string;
  origin: Vec3;
  angles: Vec3;
  keyvalues: Record<string, string>;
  connections: Connection[];
}

export interface Group {
  id: string;
  name: string;
  map: string;
  source: string;
  note?: string;
  template?: boolean;
  entities: GroupEntity[];
}

const wire = (output: string, target: string, input: string,
              param = '', delay = 0, times = -1): Connection =>
  ({ output, target, input, param, delay, times });

const relay = (targetname: string, origin: Vec3, connections: Connection[] = []): GroupEntity =>
  ({ classname: 'logic_relay', targetname, origin, angles: [0, 0, 0], keyvalues: {}, connections });

const item = (classname: string, origin: Vec3): GroupEntity =>
  ({ classname, targetname: '', origin, angles: [0, 0, 0], keyvalues: {}, connections: [] });

export const TEMPLATES: Group[] = [
  {
    id: 't_health', name: 'Health station', note: 'a charger and a few kits',
    map: '', source: 'template',
    entities: [
      item('item_healthcharger', [0, 0, 0]),
      item('item_healthkit', [-32, -24, 0]),
      item('item_healthkit', [-32, 24, 0]),
      item('item_healthvial', [-32, 0, 0]),
      item('item_battery', [32, 0, 0]),
    ],
  },
  {
    id: 't_ammo', name: 'Ammo cache', note: 'one of everything, in a row',
    map: '', source: 'template',
    entities: [
      item('item_ammo_pistol', [-64, 0, 0]),
      item('item_ammo_smg1', [-32, 0, 0]),
      item('item_ammo_ar2', [0, 0, 0]),
      item('item_box_buckshot', [32, 0, 0]),
      item('item_ammo_357', [64, 0, 0]),
      item('item_rpg_round', [96, 0, 0]),
    ],
  },
  {
    id: 't_ticker', name: 'Ticking relay',
    note: 'a timer firing a relay every two seconds — watch it in the event log',
    map: '', source: 'template',
    entities: [
      {
        classname: 'logic_timer', targetname: 'ss_ticker_timer',
        origin: [0, 0, 0], angles: [0, 0, 0],
        keyvalues: { RefireTime: '2', StartDisabled: '0' },
        connections: [wire('OnTimer', 'ss_ticker_relay', 'Trigger')],
      },
      relay('ss_ticker_relay', [64, 0, 0]),
    ],
  },
  {
    id: 't_counter', name: 'Count to three',
    note: 'a timer feeding a counter that fires once it reaches three',
    map: '', source: 'template',
    entities: [
      {
        classname: 'logic_timer', targetname: 'ss_count_timer',
        origin: [0, 0, 0], angles: [0, 0, 0],
        keyvalues: { RefireTime: '1', StartDisabled: '0' },
        connections: [wire('OnTimer', 'ss_count_counter', 'Add', '1')],
      },
      {
        classname: 'math_counter', targetname: 'ss_count_counter',
        origin: [64, 0, 0], angles: [0, 0, 0],
        keyvalues: { StartValue: '0', Min: '0', Max: '3' },

        // OnHitMax stops the timer and announces itself, so the chain visibly
        // finishes rather than running forever.
        connections: [
          wire('OnHitMax', 'ss_count_timer', 'Disable'),
          wire('OnHitMax', 'ss_count_done', 'Trigger'),
        ],
      },
      relay('ss_count_done', [128, 0, 0]),
    ],
  },
  {
    id: 't_chain', name: 'Relay chain',
    note: 'four relays firing one after another, half a second apart',
    map: '', source: 'template',
    entities: [
      relay('ss_chain_1', [0, 0, 0], [wire('OnTrigger', 'ss_chain_2', 'Trigger', '', 0.5)]),
      relay('ss_chain_2', [64, 0, 0], [wire('OnTrigger', 'ss_chain_3', 'Trigger', '', 0.5)]),
      relay('ss_chain_3', [128, 0, 0], [wire('OnTrigger', 'ss_chain_4', 'Trigger', '', 0.5)]),
      relay('ss_chain_4', [192, 0, 0]),
    ],
  },
  {
    id: 't_onspawn', name: 'Fires on map load',
    note: 'logic_auto into a relay — the usual way a map starts something',
    map: '', source: 'template',
    entities: [
      {
        classname: 'logic_auto', targetname: 'ss_auto',
        origin: [0, 0, 0], angles: [0, 0, 0], keyvalues: {},
        connections: [wire('OnMapSpawn', 'ss_auto_relay', 'Trigger')],
      },
      relay('ss_auto_relay', [64, 0, 0]),
    ],
  },
];

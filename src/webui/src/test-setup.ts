/*
 * A clean address bar for every test.
 *
 * The page keeps what you set up to look at — which game, which pane, what the
 * list is filtered to — in the URL, so that a view can be sent to somebody as a
 * link. Tests share one jsdom document and therefore one address, and without
 * this a test that types into a filter box leaves that filter applied for
 * whatever runs next.
 */

import { beforeEach } from 'vitest';

beforeEach(() => history.replaceState(null, '', '/'));

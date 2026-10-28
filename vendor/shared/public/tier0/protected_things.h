//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// This header tries to prevent people from using potentially dangerous functions
// (like the notorious non-null-terminating strncpy) and functions that will break
// VCR mode (like time, input, registry, etc).
// This header should be included by ALL of our source code.

// Eventually, ALL of these should be protected, but one man can only accomplish so much in
// one day AND work on features too!

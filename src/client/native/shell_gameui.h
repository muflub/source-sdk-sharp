// The engine's GameUI answered in managed (plan_gameui_managed.md G4,
// shell_gameui.cpp).
#pragma once

// Swaps engine's import of the symbol lookup (dlsym; GetProcAddress on
// Windows) so the one lookup of the stock GameUI module's CreateInterface
// answers this client's GameUI factory; the import is put back at that
// lookup. Called at the first call the engine makes into the client, before
// it loads the GameUI module. False when the import is not found, and the
// stock module then answers.
bool ShellGameUi_InstallFactorySwap();

// Whether pAddress lies in the stock GameUI module (GameUI.so, GameUI.dll).
bool ShellGameUi_IsStockModule(void* pAddress);

// The menu layer's root under the GameUI root while the engine's GameUI is up,
// and under the client viewport otherwise (shell_hud.cpp); once a frame.

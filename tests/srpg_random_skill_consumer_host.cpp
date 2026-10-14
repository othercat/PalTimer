#include "pch.h"
#include "random_skill_run_state.h"
#include <fstream>
#include <iterator>
#include <iostream>

int main(int argc, char** argv) {
    if (argc != 4) return 2;
    std::ifstream file(argv[1], std::ios::binary);
    const std::string json((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    pal::random_skill_run::State state;
    std::string error;
    if (!pal::random_skill_run::Decode(json, argv[2], argv[3], state, error)) {
        std::cerr << "PALDLL decoder rejected timer-imported receipt: " << error << '\n'; return 1;
    }
    if (state.seed != UINT32_MAX || state.victory != UINT32_MAX - 2u || state.initialized != 65535u) return 1;
    for (unsigned role = 0; role < 16; ++role)
        if (state.queues[role] != std::vector<int16_t>{static_cast<int16_t>(370 + role),
            static_cast<int16_t>(2148 + role), static_cast<int16_t>(32767 - role)}) return 1;
    if (pal::random_skill_run::Decode(json, "5.RPG", argv[3], state, error)) return 1;
    std::string wrongHash = argv[3]; wrongHash[0] = wrongHash[0] == 'a' ? 'b' : 'a';
    if (pal::random_skill_run::Decode(json, argv[2], wrongHash, state, error)) return 1;
    std::cout << "PASS: actual PALDLL decoder accepts all 16 timer-imported queues and uint32 boundaries; wrong slot/hash rejected\n";
}

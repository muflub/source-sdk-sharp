// Random vectors for the muzzle flashes' scatter: three independent draws, each uniform over
// [flLo, flHi). Repeatable from run to run, because the generator is default-seeded.

#pragma once

#include <random>

class CVectorRandom
{
public:
	void Fill(float flLo, float flHi, float* pOutXYZ)
	{
		for (int i = 0; i < 3; i++)
			pOutXYZ[i] = flLo + std::generate_canonical<float, 24>(m_Generator) * (flHi - flLo);
	}

private:
	std::minstd_rand m_Generator;
};

namespace Server.Nocthemar.Core;

/// <summary>Result of one attack roll: the damage to deal and whether it was a critical hit.</summary>
public readonly record struct AttackRoll(int Damage, bool Crit);

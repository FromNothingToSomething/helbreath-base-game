import type { QuestProgress } from './NocView';
import type { QuestView } from './FactionView';
import { tierName } from './FactionView';

export type QuestStatus = 'available' | 'active' | 'complete' | 'done' | 'locked';

export interface QuestContext {
    factionId: number | null;
    /** Current tier with the quest's faction. */
    tier: number;
    active: readonly QuestProgress[];
    done: readonly number[];
    maxActive: number;
}

export interface QuestState {
    status: QuestStatus;
    /** Progress clamped to the objective count; 0 when not active. */
    progress: number;
    /** Why a locked quest cannot be accepted; same wording as the server. */
    reason?: string;
}

/** Client preview of the server's accept / turn-in rules; the server stays authoritative. */
export function questState(quest: QuestView, ctx: QuestContext): QuestState {
    const active = ctx.active.find((a) => a.questId === quest.id);
    if (active) {
        const progress = Math.min(quest.count, Math.max(0, active.progress));
        return { status: progress >= quest.count ? 'complete' : 'active', progress };
    }
    if (ctx.done.includes(quest.id)) {
        return { status: 'done', progress: 0 };
    }
    if (ctx.factionId === null) {
        return { status: 'locked', progress: 0, reason: 'Join a faction first.' };
    }
    if (ctx.factionId !== quest.factionId) {
        return { status: 'locked', progress: 0, reason: 'Quest of another faction.' };
    }
    if (ctx.tier < quest.minTier) {
        return { status: 'locked', progress: 0, reason: `Requires ${tierName(quest.minTier)} standing.` };
    }
    if (ctx.active.length >= ctx.maxActive) {
        return { status: 'locked', progress: 0, reason: 'Too many active quests.' };
    }
    return { status: 'available', progress: 0 };
}

/** "Kill 3/10" style objective line; names come from the caller (monster / item catalogs live outside this module). */
export function objectiveText(quest: QuestView, progress: number, targetName: string): string {
    const verb = quest.kind === 'kill' ? 'Defeat' : 'Deliver';
    return `${verb} ${targetName}: ${Math.min(quest.count, Math.max(0, progress))} / ${quest.count}`;
}

import type { Scene } from 'phaser';
import { HIGH_DEPTH } from '../../Config';
import { TILE_SIZE } from '../assets/HBMap';
import { factionColor, factionColorLight } from '../../ui/factionTheme';

const FONT_SIZE_PX = 11;
const ICON_SIZE = 16;
const GAP = 2;

/**
 * Name (10-12 px, black outline) centered above a player, with a 16x16 faction icon to its left.
 * The name takes the faction's light color when the faction is shown, parchment otherwise.
 */
export class PlayerTag {
    private readonly text: Phaser.GameObjects.Text;
    private readonly icon: Phaser.GameObjects.Graphics;
    private iconFactionId: number | null = null;
    private showIcon = false;

    constructor(scene: Scene) {
        this.text = scene.add.text(0, 0, '', {
            fontFamily: 'Georgia, serif',
            fontSize: `${FONT_SIZE_PX}px`,
            color: '#f0e6c8',
            stroke: '#000000',
            strokeThickness: 3,
        });
        this.text.setOrigin(0.5, 1);
        this.text.setDepth(HIGH_DEPTH);
        this.icon = scene.add.graphics();
        this.icon.setDepth(HIGH_DEPTH);
        this.setVisible(false);
    }

    /** `name` may be empty (name hidden); `factionId` null = no icon. */
    public setContent(name: string, factionId: number | null, showFaction: boolean): void {
        this.text.setText(name);
        this.text.setColor(showFaction && factionId !== null ? factionColorLight(factionId) : '#f0e6c8');
        this.showIcon = showFaction && factionId !== null;
        if (this.iconFactionId !== factionId) {
            this.iconFactionId = factionId;
            this.redrawIcon();
        }
        this.setVisible(name !== '' || this.showIcon);
    }

    public setVisible(visible: boolean): void {
        this.text.setVisible(visible && this.text.text !== '');
        this.icon.setVisible(visible && this.showIcon);
    }

    /** Anchors the tag above the sprite whose feet are at (pixelX, pixelY). */
    public setPosition(pixelX: number, pixelY: number): void {
        const baseY = pixelY - 2 * TILE_SIZE;
        const hasText = this.text.text !== '';
        const textWidth = hasText ? this.text.width : 0;
        const total = (this.showIcon ? ICON_SIZE + (hasText ? GAP : 0) : 0) + textWidth;
        const left = pixelX - total / 2;
        const textLeft = left + (this.showIcon ? ICON_SIZE + (hasText ? GAP : 0) : 0);
        this.text.setPosition(textLeft + textWidth / 2, baseY);
        this.icon.setPosition(left, baseY - ICON_SIZE + 2);
    }

    public destroy(): void {
        this.text.destroy();
        this.icon.destroy();
    }

    private redrawIcon(): void {
        this.icon.clear();
        if (this.iconFactionId === null) {
            return;
        }
        const color = Phaser.Display.Color.HexStringToColor(factionColor(this.iconFactionId)).color;
        // shield: black outline, faction fill
        this.icon.fillStyle(0x000000, 1);
        this.icon.fillRect(1, 0, ICON_SIZE - 2, ICON_SIZE - 5);
        this.icon.fillTriangle(1, ICON_SIZE - 5, ICON_SIZE - 1, ICON_SIZE - 5, ICON_SIZE / 2, ICON_SIZE);
        this.icon.fillStyle(color, 1);
        this.icon.fillRect(3, 2, ICON_SIZE - 6, ICON_SIZE - 8);
        this.icon.fillTriangle(3, ICON_SIZE - 6, ICON_SIZE - 3, ICON_SIZE - 6, ICON_SIZE / 2, ICON_SIZE - 3);
    }
}

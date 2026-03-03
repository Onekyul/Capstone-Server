import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
    vus: 50,
    duration: '30s',
    thresholds: {
        http_req_duration: ['avg<1000'],
    },
};

export default function () {
    const payload = JSON.stringify({
        userId: 1,
        nickname: "benchUser",
        stage: 5,
        equip: { weapon: "sword_01", helmet: "helm_01", armor: "armor_01", boots: "boots_01" },
        inventory: [
            { id: "item_001", count: 10 },
            { id: "item_002", count: 5 },
            { id: "item_003", count: 3 }
        ],
        equipments: [
            { id: "equip_001", level: 3 },
            { id: "equip_002", level: 1 }
        ],
        enchants: [
            { id: "ench_001", level: 2 }
        ]
    });

    http.post('http://localhost:7000/api/game/save-direct', payload, {
        headers: { 'Content-Type': 'application/json' },
    });
}

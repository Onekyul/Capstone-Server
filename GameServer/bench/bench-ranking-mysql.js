import http from 'k6/http';

export const options = {
    vus: 50,
    duration: '30s',
    thresholds: {
        http_req_duration: ['avg<1000'],
    },
};

export default function () {
    http.get('http://localhost:7000/api/ranking/boss-mysql?top=10');
}

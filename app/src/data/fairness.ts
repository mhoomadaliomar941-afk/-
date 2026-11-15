import { sha256 } from 'js-sha256';

// Same formula as assign_owner() in replica/schema.sql:
// winning spot = (first 60 bits of sha256(seed || beacon)) mod total + 1
export function winningSpot(serverSeed: string, beaconValue: string, totalSpots: number): number {
  const hex = sha256(serverSeed + beaconValue).slice(0, 15);
  return Number(BigInt('0x' + hex) % BigInt(totalSpots)) + 1;
}
export function seedHash(serverSeed: string): string {
  return sha256(serverSeed);
}
export function verify(a: { serverSeed: string; beaconValue: string; winningSpot: number }, publishedHash: string, totalSpots: number) {
  return seedHash(a.serverSeed) === publishedHash && winningSpot(a.serverSeed, a.beaconValue, totalSpots) === a.winningSpot;
}
